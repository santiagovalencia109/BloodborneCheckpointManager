// SPDX-License-Identifier: GPL-3.0-or-later
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <MinHook.h>
#include <atomic>
#include <array>
#include <vector>
#include <algorithm>
#include <cstring>
#include "hooks.h"
#include "layout.h"
#include "startup_detour.h"
#define SYSV __attribute__((sysv_abi))
extern "C" void SYSV BcmTickHook();
extern "C" { void* BcmTickOriginal=nullptr; }
namespace external {
static uintptr_t host, image;
static SRWLOCK gate=SRWLOCK_INIT;
static CONDITION_VARIABLE changed=CONDITION_VARIABLE_INIT;
static unsigned active;
static bool frozen;
static uint64_t epoch;
static uint64_t saveWrites;
static thread_local unsigned depth;
static std::atomic<bool> blocked{false}, world{false}, loading{true}, title{false};
static std::atomic<int> command{0}, result{0}, echoes{-1}, level{-1}, vials{-1}, bullets{-1};
static unsigned phase;
static std::atomic<bool> installed{false}, tickReady{false};
static std::atomic<int> tickStatus{-100};
static std::array<void*,128> originals{};
static std::array<bool,128> writers{};
static std::array<bool,128> fdWriters{};
static bool SaveFd(uint64_t fd);
static bool Read(uintptr_t address,void* data,size_t size) {
    SIZE_T read=0; return address && ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(address),data,size,&read) && read==size;
}
template<class T> static T Field(uintptr_t base,size_t offset=0,T fallback={}) {
    T value{}; return base && Read(base+offset,&value,sizeof(value)) ? value : fallback;
}
static bool Bytes(size_t offset,std::initializer_list<unsigned char> value) {
    unsigned char data[32]; return value.size()<=32 && Read(image+offset,data,value.size()) && !std::memcmp(data,value.begin(),value.size());
}
static void Enter() {
    if (depth++) return;
    AcquireSRWLockExclusive(&gate);
    while (frozen) SleepConditionVariableSRW(&changed,&gate,INFINITE,0);
    ++active; ReleaseSRWLockExclusive(&gate);
}
static void Leave(bool writing,bool saved) {
    AcquireSRWLockExclusive(&gate);
    if (writing) ++epoch;
    if (saved) ++saveWrites;
    if (!--depth) { --active; WakeAllConditionVariable(&changed); }
    ReleaseSRWLockExclusive(&gate);
}
// All resolver exports use integer/pointer arguments (at most six) and the guest SysV ABI.
// Host helper functions use Windows x64. Separate wrappers preserve the appropriate register layout.
template<size_t N> static uint64_t SYSV GuestIo(uint64_t a,uint64_t b,uint64_t c,uint64_t d,uint64_t e,uint64_t f) {
    Enter(); const bool save=fdWriters[N] && SaveFd(a);
    using Fn=uint64_t(SYSV*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t);
    auto value=reinterpret_cast<Fn>(originals[N])(a,b,c,d,e,f); Leave(writers[N],save && int64_t(value)>0); return value;
}
template<size_t N> static uint64_t HostIo(uint64_t a,uint64_t b,uint64_t c,uint64_t d,uint64_t e,uint64_t f) {
    Enter(); const bool save=fdWriters[N] && SaveFd(a);
    using Fn=uint64_t(*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t);
    auto value=reinterpret_cast<Fn>(originals[N])(a,b,c,d,e,f); Leave(writers[N],save && int64_t(value)>0); return value;
}
template<size_t... N> static auto GuestHooks(std::index_sequence<N...>) { return std::array<void*,sizeof...(N)>{reinterpret_cast<void*>(&GuestIo<N>)...}; }
template<size_t... N> static auto HostHooks(std::index_sequence<N...>) { return std::array<void*,sizeof...(N)>{reinterpret_cast<void*>(&HostIo<N>)...}; }
static auto guestHooks=GuestHooks(std::make_index_sequence<128>{});
static auto hostHooks=HostHooks(std::make_index_sequence<128>{});
static bool SaveFd(uint64_t fd) {
    if (fd<3 || fd>=1024) return false;
    auto mutex=reinterpret_cast<SRWLOCK*>(host+runtime::At(runtime::FileLock));
    AcquireSRWLockExclusive(mutex);
    const auto file=host+runtime::At(runtime::Files)+fd*runtime::At(runtime::FileStride);
    char path[512];
    const bool saved=Field<int>(file) && Read(file+runtime::At(runtime::FilePathOffset),path,sizeof(path)) && std::memchr(path,0,sizeof(path)) && !std::strncmp(path,"/savedata",9);
    ReleaseSRWLockExclusive(mutex); return saved;
}
static int Handles() {
    // File record size/path are supplied by the validated legacy or atomic-save contract.
    // Inspect under the runtime's own file mutex, including handles opened before attachment.
    auto mutex=reinterpret_cast<SRWLOCK*>(host+runtime::At(runtime::FileLock));
    AcquireSRWLockExclusive(mutex);
    int count=0;
    for (unsigned n=3;n<1024;++n) {
        const auto file=host+runtime::At(runtime::Files)+n*runtime::At(runtime::FileStride);
        if (!Field<int>(file)) continue;
        char path[512];
        if (!Read(file+runtime::At(runtime::FilePathOffset),path,sizeof(path)) || !std::memchr(path,0,sizeof(path))) { count=1024; break; }
        if (!std::strncmp(path,"/savedata",9)) ++count;
    }
    ReleaseSRWLockExclusive(mutex); return count;
}
static void Neutral(unsigned char* data) {
    std::memset(data,0,4); std::memset(data+4,128,4); std::memset(data+8,0,4);
    data[52]=0; std::memset(data+56,0,20);
}
static SRWLOCK padLock=SRWLOCK_INIT;
static uint32_t held;
static bool previousBlock;
static void Filter(unsigned char* data) {
    if (!data) return;
    AcquireSRWLockExclusive(&padLock);
    uint32_t buttons=0; std::memcpy(&buttons,data,4);
    const bool block=blocked.load();
    if (block) { held|=buttons; previousBlock=true; Neutral(data); }
    else {
        if (previousBlock) { held|=buttons; previousBlock=false; }
        held&=buttons;
        if (held) Neutral(data);
    }
    ReleaseSRWLockExclusive(&padLock);
}
using PadState=int32_t(SYSV*)(int32_t,unsigned char*);
using PadRead=int32_t(SYSV*)(int32_t,unsigned char*,int32_t);
static PadState padState;
static PadRead padRead;
static thread_local unsigned padDepth;
using AttachMain=void(*)();
static AttachMain attachMain;
static bool GameVerified();
static void MainAttach() {
    attachMain();
    if(tickReady.load()) return;
    // probe has now protected executable guest segments. Earlier in GPU startup
    // they are writable data, so installing a guest-code hook there is invalid.
    // Recheck after the launcher's community patches and native binding relocations.
    tickStatus=-101;
    if(!GameVerified()) { tickStatus=-102; return; }
    // Awaken reserves the surrounding guest address space, so a near MinHook
    // relay cannot be allocated. Install the reviewed absolute jump before guest execution.
    if(InstallStartupTick(reinterpret_cast<void*>(image+0x2034770),reinterpret_cast<void*>(&BcmTickHook),&BcmTickOriginal,host)) {
        tickStatus=0; tickReady=true;
    } else tickStatus=-103;
}
std::string StartupStatus() {
    const int status=tickStatus.load();
    if(status==-100 && Field<int>(host,runtime::At(runtime::Entered)))
        return "The game passed the startup connection boundary. Restart it with the manager open";
    return status==-100 ? "Waiting for the runtime startup boundary" : status==-101 ? "Checking guest startup code" :
        status==-102 ? "Guest code changed at the startup boundary" : status==-103 ? "Could not install the startup tick detour" : std::string("Tick activation: ")+MH_StatusToString(MH_STATUS(status));
}
bool Ready() { return installed.load() && tickReady.load(); }
bool Starting() { return installed.load() && !tickReady.load() && tickStatus.load()>=-101 &&
    Field<int>(host,runtime::At(runtime::Entered))==0; }
static int32_t SYSV ReadState(int32_t handle,unsigned char* data) {
    ++padDepth; const auto status=padState(handle,data); --padDepth;
    if (!padDepth && !status) Filter(data); return status;
}
static int32_t SYSV ReadPad(int32_t handle,unsigned char* data,int32_t count) {
    ++padDepth; const auto status=padRead(handle,data,count); --padDepth;
    if (!padDepth && status>0 && status<=count && count<=64) for (int n=0;n<status;++n) Filter(data+n*120);
    return status;
}
void BlockInput(bool value) { blocked=value; }
bool InputBlocked() { return blocked.load(); }
bool Frozen() { AcquireSRWLockExclusive(&gate); bool value=frozen; ReleaseSRWLockExclusive(&gate); return value; }
void Release() {
    AcquireSRWLockExclusive(&gate); frozen=false; WakeAllConditionVariable(&changed); ReleaseSRWLockExclusive(&gate); blocked=false;
}
State Snapshot() {
    AcquireSRWLockExclusive(&gate); auto generation=epoch, writes=saveWrites; ReleaseSRWLockExclusive(&gate);
    return {world.load(),loading.load(),title.load(),echoes.load(),level.load(),vials.load(),bullets.load(),Handles(),generation,writes};
}
static int Goods(int id) {
    auto rec=Field<uintptr_t>(Field<uintptr_t>(image,0x553b130),8);
    if (!rec) return -1;
    auto list=rec+0x328;
    if (!Field<uintptr_t>(list,0x68) || !Field<uintptr_t>(list,0x78)) return -1;
    using Find=int32_t(SYSV*)(uintptr_t,uint32_t,uint32_t);
    const int index=reinterpret_cast<Find>(image+0x14d9e80)(list,0x40000000,id);
    if (index<0) return 0;
    const auto last=Field<int>(list,0x88,-1);
    const auto split=Field<uint32_t>(list,0x24);
    if (last<0 || uint32_t(index)>uint32_t(last)) return -1;
    const auto entry=uint32_t(index)<split ? Field<uintptr_t>(list,0x58)+16ull*uint32_t(index) :
        Field<uintptr_t>(list,0x48)+16ull*(uint32_t(index)-split);
    return Field<int>(entry)==0 ? 0 : Field<int>(entry,8,-1);
}
void Tick() {
    auto wc=Field<uintptr_t>(image,0x553e878), session=Field<uintptr_t>(image,0x5540290);
    world=wc && Field<uintptr_t>(wc,0x60) && session;
    auto helper=Field<uintptr_t>(image,0x553e8b8);
    const bool frame=Field<unsigned char>(helper,0x50)||Field<unsigned char>(helper,0x51);
    loading=Field<unsigned char>(image,0x556286b)&&!frame;
    if (phase==1 && !world.load()) phase=2;
    if (phase==2 && !world.load() && !loading.load()) phase=3;
    if (phase==3 && (world.load()||loading.load())) phase=0;
    title=phase==3;
    const auto rec=Field<uintptr_t>(Field<uintptr_t>(image,0x553b130),8);
    echoes=Field<int>(rec,0x94,-1); level=Field<int>(rec,0x90,-1);
    static ULONGLONG inventory=0;
    if (world.load() && !loading.load() && GetTickCount64()-inventory>250) {
        vials=Goods(1000); bullets=Goods(900); inventory=GetTickCount64();
    }
    const int op=command.exchange(0); if (!op) return;
    if (!world.load() || loading.load() || (Field<unsigned char>(Field<uintptr_t>(Field<uintptr_t>(image,0x5540058),8),0x168)&1)) { result=-1; return; }
    const auto gsm=Field<uintptr_t>(image,0x5556678);
    if (op==1) {
        if (!gsm || !Field<unsigned char>(gsm,0x1578) || !Field<uintptr_t>(image,0x553b0c8)) { result=-1; return; }
        using Save=void(SYSV*)(); reinterpret_cast<Save>(image+0x1336da0)(); result=1;
    } else if (op==2) {
        auto step=Field<uintptr_t>(Field<uintptr_t>(image,0x553b108),0x10); uint32_t two=2; SIZE_T wrote=0;
        if (!step || !WriteProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(step+8),&two,4,&wrote) || wrote!=4) { result=-1; return; }
        phase=1; result=1;
    }
}
bool Acquire(bool restoring) {
    if (!Ready() || Frozen()) return false;
    const auto before=Snapshot().saveWrites;
    result=0; BlockInput(restoring); command=restoring ? 2 : 1;
    const auto deadline=GetTickCount64()+45000;
    while (GetTickCount64()<deadline && result.load()==0) Sleep(10);
    if (result.load()!=1) { command=0; BlockInput(false); return false; }
    uint64_t last=0; auto steady=GetTickCount64();
    while (GetTickCount64()<deadline) {
        auto state=Snapshot();
        const auto gsm=Field<uintptr_t>(image,0x5556678);
        const bool saved=gsm && Field<unsigned char>(gsm,0x1548,1)==0 && state.saveWrites!=before;
        const bool ready=restoring ? state.title : saved && state.world && !state.loading;
        if (!ready || state.handles || state.epoch!=last) { steady=GetTickCount64(); last=state.epoch; }
        if (ready && !state.handles && GetTickCount64()-steady>=750) {
            AcquireSRWLockExclusive(&gate);
            // No calls can begin between this check and publishing the frozen gate.
            // Every host helper and guest filesystem/savedata export is gated.
            const bool safe=!active && !frozen && !Handles();
            if (safe) frozen=true;
            ReleaseSRWLockExclusive(&gate);
            if (safe) {
                const bool current=restoring ? title.load() && !world.load() && !loading.load() :
                    world.load() && !loading.load() && Field<unsigned char>(gsm,0x1548,1)==0;
                if (current) return true;
                Release(); return false;
            }
        }
        Sleep(10);
    }
    BlockInput(false); return false;
}
static bool GameVerified() {
    return Bytes(0x1336da0,{0x55,0x48,0x89,0xe5,0x48,0x8d,0x05,0x1d,0x43,0x20,0x04,0x48,0x83,0x38,0x00,0x75}) &&
        (VerifiedAwakenFrame(reinterpret_cast<void*>(image+0x2034770),host) || Bytes(0x2034770,{0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,0x41,0x54,0x53,0x48,0x83,0xec,0x38})) &&
        Bytes(0x14d9e80,{0x53,0x81,0xe6,0x00,0x00,0x00,0xf0,0x81,0xe2,0xff,0xff,0xff,0x0f}) &&
        Bytes(0x17c13ce,{0x48,0x8d,0x1d,0x33,0x9d,0xd7,0x03,0x48,0x8b,0x03,0x48,0x85,0xc0,0x75,0x24}) &&
        Bytes(0x17c13fe,{0x48,0x8b,0x03,0x48,0x8b,0x40,0x10,0xc7,0x40,0x08,0x02,0x00,0x00,0x00});
}
bool Install(uintptr_t base,std::string& error) {
    if (installed.load()) return true;
    host=base; image=Field<uintptr_t>(host,runtime::At(runtime::Image));
    if (!GameVerified()) {
        error="Game code differs from the verified 1.09 image"; return false;
    }
    // Establish complete operation coverage before guest execution begins. Attaching
    // after startup remains read-only: pre-existing calls might otherwise bypass wrappers.
    if (Field<int>(host,runtime::At(runtime::Entered)) || Handles()) {
        error="The game started before the bridge. Restart it with the manager's external watcher active"; return false;
    }
    if (MH_Initialize()!=MH_OK) { error="Hook library initialization failed"; return false; }
    std::vector<void*> targets;
    auto hook=[&](void* target,void* detour,void** original) {
        auto status=MH_CreateHook(target,detour,original);
        if (status!=MH_OK) { error="Hook "+std::to_string(reinterpret_cast<uintptr_t>(target)-host)+": "+MH_StatusToString(status); return false; }
        targets.push_back(target); return true;
    };
    // The runtime resolvers take PS4 NIDs, not human-readable names. Read the
    // resolved and validated RuntimeExport tables instead; each entry is {name, function}.
    using Resolve=uintptr_t(*)(const char*);
    auto lookup=[](uintptr_t table,unsigned count,const char* wanted)->uintptr_t {
        for (unsigned n=0;n<count;++n) {
            const auto name=Field<uintptr_t>(table,n*16), fn=Field<uintptr_t>(table,n*16+8);
            char text[128];
            if (Read(name,text,sizeof(text)) && std::memchr(text,0,sizeof(text)) && !std::strcmp(text,wanted)) return fn;
        }
        return 0;
    };
    static auto files=+[](const char* name)->uintptr_t {
        for (unsigned n=0;n<runtime::At(runtime::FileCount);++n) {
            const auto text=Field<uintptr_t>(host+runtime::At(runtime::FileTable),n*16); char value[128];
            if (Read(text,value,sizeof(value)) && std::memchr(value,0,sizeof(value)) && !std::strcmp(value,name)) return Field<uintptr_t>(host+runtime::At(runtime::FileTable),n*16+8);
        } return 0;
    };
    static auto saves=+[](const char* name)->uintptr_t {
        for (unsigned n=0;n<runtime::At(runtime::SaveCount);++n) {
            const auto text=Field<uintptr_t>(host+runtime::At(runtime::SaveTable),n*16); char value[128];
            if (Read(text,value,sizeof(value)) && std::memchr(value,0,sizeof(value)) && !std::strcmp(value,name)) return Field<uintptr_t>(host+runtime::At(runtime::SaveTable),n*16+8);
        } return 0;
    };
    const char* fileNames[]={"sceKernelOpen","open","sceKernelClose","close","sceKernelRead","read","sceKernelPread","pread","sceKernelWrite","write","sceKernelPwrite","pwrite","sceKernelLseek","lseek","sceKernelFstat","fstat","sceKernelStat","stat","sceKernelGetdirentries","getdirentries","sceKernelRename","rename","sceKernelFtruncate","ftruncate","sceKernelTruncate","truncate","sceKernelFsync","fsync","access","sceKernelCheckReachability","sceKernelMkdir","mkdir","sceKernelRmdir","rmdir","sceKernelUnlink","unlink"};
    const char* saveNames[]={"sceSaveDataInitialize","sceSaveDataInitialize2","sceSaveDataInitialize3","sceSaveDataTerminate","sceSaveDataMount","sceSaveDataMount2","sceSaveDataUmount","sceSaveDataSetParam","sceSaveDataSaveIcon","sceSaveDataDelete","sceSaveDataDirNameSearch","sceSaveDataSetupSaveDataMemory","sceSaveDataGetSaveDataMemory","sceSaveDataSetSaveDataMemory"};
    size_t index=0; bool ok=true;
    auto exports=[&](auto& names,Resolve resolve) {
        for (const auto name:names) {
            auto target=reinterpret_cast<void*>(resolve(name));
            if (!target) { error=std::string("Missing runtime export: ")+name; ok=false; break; }
            if (std::find(targets.begin(),targets.end(),target)!=targets.end()) continue;
            writers[index]=std::strstr(name,"Write") || std::strstr(name,"write") || std::strstr(name,"truncate") ||
                std::strstr(name,"Close") || std::strstr(name,"close") || std::strstr(name,"Rename") ||
                std::strstr(name,"rename") || std::strstr(name,"Unlink") || std::strstr(name,"unlink") ||
                std::strstr(name,"Mkdir") || std::strstr(name,"mkdir") || std::strstr(name,"Rmdir") ||
                std::strstr(name,"rmdir") || std::strncmp(name,"sceSaveData",11)==0;
            fdWriters[index]=!std::strcmp(name,"sceKernelWrite") || !std::strcmp(name,"write") ||
                !std::strcmp(name,"sceKernelPwrite") || !std::strcmp(name,"pwrite");
            if (!hook(target,guestHooks[index],&originals[index])) { ok=false; break; } ++index;
        }
    };
    exports(fileNames,files); if (ok) exports(saveNames,saves);
    // Only these host ABI helpers survive LTO. All guest file/save exports above remain gated.
    const size_t helpers[]={runtime::At(runtime::Open),runtime::At(runtime::Close),runtime::At(runtime::Read)};
    if (ok) for (auto offset:helpers) { writers[index]=offset==runtime::At(runtime::Open) || offset==runtime::At(runtime::Close) || offset==runtime::At(runtime::Write); fdWriters[index]=offset==runtime::At(runtime::Write); if (!hook(reinterpret_cast<void*>(host+offset),hostHooks[index],&originals[index])) { ok=false; break; } ++index; }
    if (ok) ok=hook(reinterpret_cast<void*>(lookup(host+runtime::At(runtime::PadTable),runtime::At(runtime::PadCount),"scePadReadState")),reinterpret_cast<void*>(&ReadState),reinterpret_cast<void**>(&padState));
    if (ok) ok=hook(reinterpret_cast<void*>(lookup(host+runtime::At(runtime::PadTable),runtime::At(runtime::PadCount),"scePadRead")),reinterpret_cast<void*>(&ReadPad),reinterpret_cast<void**>(&padRead));
    if (ok) ok=hook(reinterpret_cast<void*>(host+runtime::At(runtime::MainAttach)),reinterpret_cast<void*>(&MainAttach),reinterpret_cast<void**>(&attachMain));
    if (ok) {
        auto status=MH_EnableHook(MH_ALL_HOOKS);
        // The conservative thread freezer can encounter an exiting startup worker.
        // Retry only before guest entry; every attempt still suspends and checks all threads.
        for(unsigned retry=0; status==MH_ERROR_MEMORY_ALLOC && retry<3 &&
            !Field<int>(host,runtime::At(runtime::Entered)); ++retry) {
            Sleep(10); status=MH_EnableHook(MH_ALL_HOOKS);
        }
        ok=status==MH_OK;
        if (!ok) error=std::string("Hook activation: ")+MH_StatusToString(status);
    }
    if (!ok) {
        if (MH_DisableHook(MH_ALL_HOOKS)==MH_OK) { for (auto target:targets) MH_RemoveHook(target); MH_Uninitialize(); }
        error+="; live operations remain disabled"; return false;
    }
    installed=true; return true;
}
}
extern "C" void BcmTick() { external::Tick(); }
extern "C" BOOL BcmBeforeHooks(BOOL enabling) {
    return !enabling || external::Field<int>(external::host,runtime::At(runtime::Entered))==0;
}

// SPDX-License-Identifier: GPL-3.0-or-later
#include "checkpoint_bridge.h"
#ifdef _WIN32
#include "checkpoint_io.h"
#include "checkpoint_title.h"
#include "party/coop_hooks.h"
#include "party/game_state.h"
#include "bbport_game_menu.h"
#include <windows.h>
#include <objbase.h>
#include <sddl.h>
#include <nlohmann/json.hpp>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <string>
#include <thread>
extern "C" const char* runtime_file_user_dir(void);
extern "C" const char* runtime_savepolicy_dir(void);
extern "C" void runtime_file_save_activity(std::uint64_t*, int*);
namespace checkpoint_bridge {
using Json = nlohmann::json;
static std::atomic<int> command{0}, result{0};
static std::atomic<bool> world{false}, title{false}, loading{true};
static std::atomic<std::uint64_t> gsm{0};
static std::atomic<int> echoes{-1}, level{-1};
static std::atomic<int> vials{-1}, bullets{-1};
static std::atomic<std::uint32_t> map{0xffffffff};
static std::atomic<std::int64_t> title_lookup{0};
// Main-thread state: 1=request issued, 2=world unloaded, 3=exit load finished.
static int return_phase;
static bool restore_enabled;
static std::int64_t Ms() { return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count(); }
static std::uint64_t Slot(std::uint64_t offset) { std::uint64_t p=0; coop::SafeGet(coop::Guest(offset), &p); return p; }
static bool Writable(std::uint64_t at, std::size_t size) {
    MEMORY_BASIC_INFORMATION info{};
    if (!at || !VirtualQuery(reinterpret_cast<void*>(at), &info, sizeof(info)) || info.State!=MEM_COMMIT || info.Protect & (PAGE_GUARD|PAGE_NOACCESS)) return false;
    const DWORD p=info.Protect & 0xff;
    return (p==PAGE_READWRITE || p==PAGE_EXECUTE_READWRITE || p==PAGE_WRITECOPY || p==PAGE_EXECUTE_WRITECOPY) &&
        at + size <= reinterpret_cast<std::uint64_t>(info.BaseAddress) + info.RegionSize;
}
static BB_COOP_SYSV void Tick(std::uint64_t, std::uint64_t, std::uint64_t, std::uint64_t, std::uint64_t, std::uint64_t) {
    const auto state=coop::ReadGameState(false);
    world=state.world_up; loading=state.loading; gsm=Slot(0x5556678);
    echoes=state.echoes; level=state.level; map=state.map_id;
    // Verified upstream goods IDs; the helper also verifies the inventory
    // lookup's 1.09 machine bytes before calling it on this main thread.
    static std::int64_t inventory_read=0;
    if (state.world_up && !state.loading && Ms()-inventory_read>=250) {
        vials=coop::GoodsCount(1000); bullets=coop::GoodsCount(900); inventory_read=Ms();
    } else if (!state.world_up || state.loading) { vials=-1; bullets=-1; }
    // Query the three known IDs directly: the bounded recent-ID list can omit
    // title entries when more than 64 messages were looked up in one frame.
    auto seen=BbGameMenu::LastLookupMs(0x61f95);
    for (const auto id : {0x61f96u,0x61f97u}) seen=std::max(seen,BbGameMenu::LastLookupMs(id));
    title_lookup=seen;
    static CheckpointTitleEvidence evidence;
    if (return_phase==1 && !state.world_up) return_phase=2;
    if (return_phase==2 && !state.world_up && !state.loading) return_phase=3;
    else if (return_phase==3 && (state.world_up || state.loading)) return_phase=0;
    // The verified exit request first reaches the title splash, before its
    // command menu is built. With input blocked, that completed unload is a
    // safe restore point; requiring Continue's text would require user input.
    title=evidence.Observe(state.world_up,state.loading,seen) || return_phase==3;
    static int previous=-1;
    const int status=(state.world_up ? 1 : 0) | (state.loading ? 2 : 0) | (title ? 4 : 0);
    if (status!=previous) {
        std::printf("Checkpoint: state world=%d loading=%d title=%d lookup=%lld floor=%lld\n",
            state.world_up,state.loading,title.load(),(long long)seen,(long long)evidence.floor);
        previous=status;
    }
    const int request=command.exchange(0);
    if (!request) return;
    if (request==2 && title) { result=1; return; }
    if (!state.world_up || state.loading || coop::CutscenePlaying()) { result=-1; return; }
    if (request==1) {
        std::uint8_t allowed=0;
        if (!gsm || !Slot(0x553b0c8) || !coop::SafeGet(gsm.load()+0x1578, &allowed) || !allowed) { result=-1; return; }
        using Fn = void(BB_COOP_SYSV*)();
        reinterpret_cast<Fn>(coop::Guest(0x1336da0))();
        std::puts("Checkpoint: main-thread SaveRequest issued"); result=1;
    } else if (request==2) {
        const auto event=Slot(0x553b108); std::uint64_t step=0;
        if (!event || !coop::SafeGet(event+0x10, &step) || !Writable(step+8,4)) { result=-1; return; }
        // Same request as the byte-verified 1.09 EMEVD 2003[27] handler.
        *reinterpret_cast<volatile std::uint32_t*>(step+8)=2;
        return_phase=1;
        std::puts("Checkpoint: main-thread return-to-title requested"); result=1;
    }
}
static std::string GuidText() {
    GUID id{}; if (CoCreateGuid(&id)!=S_OK) throw std::runtime_error("Cannot create lease ID");
    char text[40]; std::snprintf(text,sizeof(text),"%08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",static_cast<unsigned long>(id.Data1), id.Data2,id.Data3,id.Data4[0],id.Data4[1],id.Data4[2],id.Data4[3],id.Data4[4],id.Data4[5],id.Data4[6],id.Data4[7]); return text;
}
static std::string Source() { return std::filesystem::absolute(std::filesystem::path(runtime_file_user_dir()) / runtime_savepolicy_dir()).lexically_normal().string(); }
static bool Send(HANDLE pipe, Json value) {
    const auto text=value.dump()+"\n"; DWORD written=0;
    return WriteFile(pipe,text.data(),static_cast<DWORD>(text.size()),&written,nullptr) && written==text.size();
}
static bool Receive(HANDLE pipe, std::string& line, std::int64_t deadline) {
    line.clear();
    while (Ms()<deadline) {
        DWORD available=0;
        if (!PeekNamedPipe(pipe,nullptr,0,nullptr,&available,nullptr)) return false;
        if (!available) { Sleep(5); continue; }
        char c; DWORD read=0;
        if (!ReadFile(pipe,&c,1,&read,nullptr) || read!=1) return false;
        if (c=='\n') return true;
        if (line.size()>=8192) return false;
        line+=c;
    }
    return false;
}
static bool Acquire(bool restoring) {
    std::uint64_t before=0; int open=0; runtime_file_save_activity(&before,&open);
    result=0; command=restoring ? 2 : 1;
    const auto deadline=Ms()+45000;
    while (Ms()<deadline && result==0) Sleep(10);
    if (result!=1) { command=0; return false; }
    std::uint64_t last=0, last_meta=0; std::int64_t steady=Ms();
    while (Ms()<deadline) {
        std::uint64_t gen=0; runtime_file_save_activity(&gen,&open);
        const auto meta=checkpoint_io_epoch();
        std::uint8_t pending=1;
        const bool save_done=gsm && coop::SafeGet(gsm.load()+0x1548,&pending) && pending==0 && gen!=before;
        const bool ready=restoring ? title.load() : save_done && world.load() && !loading.load();
        if (!ready || open || gen!=last || meta!=last_meta) { steady=Ms(); last=gen; last_meta=meta; }
        if (ready && !open && Ms()-steady>=750 && checkpoint_io_try_freeze(restoring)) {
            // Recheck state after taking the gate. State changes reject the lease.
            if (restoring ? title.load() : world.load() && !loading.load()) return true;
            checkpoint_io_thaw(); return false;
        }
        Sleep(10);
    }
    return false;
}
static PSECURITY_DESCRIPTOR Security() {
    HANDLE token=nullptr; if (!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return nullptr;
    DWORD bytes=0; GetTokenInformation(token,TokenUser,nullptr,0,&bytes);
    std::string buffer(bytes,'\0');
    if (!GetTokenInformation(token,TokenUser,buffer.data(),bytes,&bytes)) { CloseHandle(token); return nullptr; }
    CloseHandle(token); LPSTR sid=nullptr;
    if (!ConvertSidToStringSidA(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid,&sid)) return nullptr;
    const std::string sddl="D:P(A;;GA;;;SY)(A;;GA;;;"+std::string(sid)+")"; LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor=nullptr;
    ConvertStringSecurityDescriptorToSecurityDescriptorA(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr); return descriptor;
}
static void Server() {
    auto descriptor=Security(); if (!descriptor) { std::puts("Checkpoint: pipe ACL failed; bridge disabled"); return; }
    SECURITY_ATTRIBUTES security{sizeof(security),descriptor,FALSE};
    const auto pipe_name="\\\\.\\pipe\\bcm-checkpoint-v1-"+std::to_string(GetCurrentProcessId());
    for (;;) {
        HANDLE pipe=CreateNamedPipeA(pipe_name.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_FIRST_PIPE_INSTANCE,PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,16384,16384,0,&security);
        if (pipe==INVALID_HANDLE_VALUE) { std::puts("Checkpoint: pipe creation failed"); break; }
        if (!ConnectNamedPipe(pipe,nullptr) && GetLastError()!=ERROR_PIPE_CONNECTED) { CloseHandle(pipe); continue; }
        std::string session=GuidText(), lease, line; bool mutation=false;
        auto deadline=Ms()+15000;
        while (Receive(pipe,line,deadline)) {
            try {
                const auto request=Json::parse(line); const auto op=request.value("op",std::string());
                if (op=="hello") { Send(pipe,{{"ok",true},{"protocol",1},{"pid",GetCurrentProcessId()},{"source",Source()},{"session",session},{"capture",true},{"restore",restore_enabled},{"inputBarrier",true},{"title",title.load()},{"world",world.load()},{"loading",loading.load()},{"titleLookupAgeMs",title_lookup ? Ms()-title_lookup.load() : -1},{"saveHandles",runtime_file_save_handles(0)},{"echoes",echoes.load()},{"vials",vials.load()},{"bullets",bullets.load()},{"level",level.load()},{"map",map.load()}}); deadline=Ms()+15000; continue; }
                if (request.value("session",std::string())!=session) throw std::runtime_error("Wrong session");
                if (op=="acquire") {
                    if (!lease.empty() || checkpoint_io_frozen()) throw std::runtime_error("Lease already held or recovery required");
                    const auto mode=request.value("mode",std::string());
                    if (mode!="capture" && mode!="restore") throw std::runtime_error("Unknown operation");
                    if (mode=="restore" && !restore_enabled) throw std::runtime_error("Experimental restore is not enabled");
                    const auto next_lease=GuidText();
                    checkpoint_input_block(mode=="restore");
                    if (!Acquire(mode=="restore")) {
                        checkpoint_input_block(0);
                        throw std::runtime_error(
                        "No se obtuvo acceso seguro; no se reemplazaron saves. Estado: mundo="+std::to_string(world.load())+
                        ", carga="+std::to_string(loading.load())+", titulo="+std::to_string(title.load())+
                        ", archivos abiertos="+std::to_string(runtime_file_save_handles(0)));
                    }
                    lease=next_lease; deadline=Ms()+15000;
                    Send(pipe,{{"ok",true},{"lease",lease},{"source",Source()},{"frozen",true},{"title",title.load()},{"inputBlocked",checkpoint_input_blocked()!=0}}); continue;
                }
                if (lease.empty() || request.value("lease",std::string())!=lease || !checkpoint_io_frozen()) throw std::runtime_error("No valid frozen lease");
                if (op=="begin-mutation") { mutation=true; deadline=Ms()+86400000; }
                else if (op=="release") {
                    checkpoint_io_thaw(); checkpoint_input_block(0); lease.clear(); mutation=false; deadline=Ms()+15000;
                } else if (op!="check") throw std::runtime_error("Unknown request");
                else deadline=Ms()+(mutation ? 86400000 : 15000);
                Send(pipe,{{"ok",true},{"frozen",checkpoint_io_frozen()!=0}});
            } catch (const std::exception& ex) {
                // Acquire may outlast the previous receive deadline. Give the
                // client time to read the error before disconnecting the pipe.
                deadline=Ms()+(mutation ? 86400000 : 15000);
                if (!Send(pipe,{{"ok",false},{"error",ex.what()}})) break;
            }
        }
        if (!lease.empty()) {
            if (!mutation) { checkpoint_io_thaw(); checkpoint_input_block(0); }
            else std::puts("Checkpoint: restore interrupted; I/O remains frozen. Close the game and recover using the manager.");
        }
        DisconnectNamedPipe(pipe); CloseHandle(pipe);
    }
    LocalFree(descriptor);
}
void Init(unsigned char* image, std::uint64_t size) {
    const char* enabled=std::getenv("BB_CHECKPOINT"); if (!enabled || enabled[0]!='1') return;
    const char* party=std::getenv("BB_PARTY"); if (party && *party && std::string(party)!="0") { std::puts("Checkpoint: solo only; bridge disabled"); return; }
    if (!coop::HooksInit(image,size) || !coop::MatchesOriginal(0x1336da0,{0x55,0x48,0x89,0xe5,0x48,0x8d,0x05,0x1d,0x43,0x20,0x04,0x48,0x83,0x38,0x00,0x75})) { std::puts("Checkpoint: SaveRequest differs; bridge disabled"); return; }
    // Verified from the user's 1.09 ELF and upstream EMEVD 2003[27] documentation.
    if (!coop::MatchesOriginal(0x17c13ce,{0x48,0x8d,0x1d,0x33,0x9d,0xd7,0x03,0x48,0x8b,0x03,0x48,0x85,0xc0,0x75,0x24}) ||
        !coop::MatchesOriginal(0x17c13fe,{0x48,0x8b,0x03,0x48,0x8b,0x40,0x10,0xc7,0x40,0x08,0x02,0x00,0x00,0x00})) {
        std::puts("Checkpoint: return-to-title handler differs; bridge disabled"); return;
    }
    if (!coop::HookPrologue(0x2034770,{0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,0x41,0x54,0x53,0x48,0x83,0xec,0x38},&Tick,"checkpoint main-thread tick")) return;
    const char* restore=std::getenv("BB_CHECKPOINT_EXPERIMENTAL_RESTORE"); restore_enabled=restore && restore[0]=='1';
    std::thread(Server).detach(); std::puts("Checkpoint: bridge active (protocol 1, solo)");
}
} // namespace checkpoint_bridge
#else
namespace checkpoint_bridge { void Init(unsigned char*, std::uint64_t) {} }
#endif

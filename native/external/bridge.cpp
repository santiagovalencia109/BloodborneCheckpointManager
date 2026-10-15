// SPDX-License-Identifier: GPL-3.0-or-later
// Runtime-contract external adapter. Saves remain owned by the .NET transaction manager.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <objbase.h>
#include <sddl.h>
#include <nlohmann/json.hpp>
#include <atomic>
#include <string>
#include <vector>
#include <cstring>
#include "hooks.h"
#include "layout.h"

using Json = nlohmann::json;
static char HostHash[65]{};
constexpr char Adapter[] = "supermedo-runtime-contract-v2";
static std::atomic<bool> started{false};
static uintptr_t host;
static std::string source;
static std::atomic<bool> live{false};
static std::string activationError;
static SRWLOCK startLock=SRWLOCK_INIT;

struct StartConfig { uint32_t size, protocol; wchar_t source[1024]; uint32_t layout[26]; char hash[68]; };
static_assert(sizeof(StartConfig)==2228 && runtime::Count==26, "Managed/native configuration layout must agree");
static bool Read(uintptr_t at, void* out, size_t size) {
    SIZE_T done=0;
    return at && ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(at),out,size,&done) && done==size;
}
template<class T> static T Field(uintptr_t base, size_t offset=0, T fallback={}) {
    T result{}; return base && Read(base+offset,&result,sizeof(result)) ? result : fallback;
}
static std::string Utf8(const wchar_t* value) {
    int length=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value,-1,nullptr,0,nullptr,nullptr);
    if (!length) return {};
    std::string result(length,'\0');
    WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value,-1,result.data(),length,nullptr,nullptr);
    result.pop_back(); return result;
}
static bool HostVerified() {
    wchar_t path[32768];
    if (!GetModuleFileNameW(nullptr,path,32768)) return false;
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_SEQUENTIAL_SCAN,nullptr);
    if (file==INVALID_HANDLE_VALUE) return false;
    unsigned char diskHeader[512], loadedHeader[512]; DWORD headerRead=0;
    bool headerOk=ReadFile(file,diskHeader,sizeof(diskHeader),&headerRead,nullptr) && headerRead==sizeof(diskHeader) &&
        Read(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)),loadedHeader,sizeof(loadedHeader));
    if (headerOk) {
        const auto diskDos=reinterpret_cast<const IMAGE_DOS_HEADER*>(diskHeader), loadedDos=reinterpret_cast<const IMAGE_DOS_HEADER*>(loadedHeader);
        headerOk=diskDos->e_magic==IMAGE_DOS_SIGNATURE && loadedDos->e_magic==IMAGE_DOS_SIGNATURE &&
            diskDos->e_lfanew==loadedDos->e_lfanew && diskDos->e_lfanew>0 &&
            size_t(diskDos->e_lfanew)+sizeof(IMAGE_NT_HEADERS64)<=sizeof(diskHeader);
        if (headerOk) {
            const auto diskNt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(diskHeader+diskDos->e_lfanew);
            const auto loadedNt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(loadedHeader+loadedDos->e_lfanew);
            // The loader may update ImageBase. Compare stable identity fields, not ASLR-dependent headers.
            headerOk=diskNt->Signature==IMAGE_NT_SIGNATURE && loadedNt->Signature==IMAGE_NT_SIGNATURE &&
                diskNt->FileHeader.Machine==loadedNt->FileHeader.Machine &&
                diskNt->FileHeader.TimeDateStamp==loadedNt->FileHeader.TimeDateStamp &&
                diskNt->FileHeader.NumberOfSections==loadedNt->FileHeader.NumberOfSections &&
                diskNt->OptionalHeader.SizeOfImage==loadedNt->OptionalHeader.SizeOfImage;
        }
    }
    LARGE_INTEGER beginning{};
    if (!headerOk || !SetFilePointerEx(file,beginning,nullptr,FILE_BEGIN)) { CloseHandle(file); return false; }
    BCRYPT_ALG_HANDLE algorithm=nullptr; BCRYPT_HASH_HANDLE hash=nullptr;
    DWORD size=0, got=0; bool ok=false;
    if (BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0 &&
        BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&size),sizeof(size),&got,0)>=0) {
        std::vector<unsigned char> object(size), buffer(65536);
        if (BCryptCreateHash(algorithm,&hash,object.data(),size,nullptr,0,0)>=0) {
            ok=true;
            for (;;) {
                DWORD read=0;
                if (!ReadFile(file,buffer.data(),DWORD(buffer.size()),&read,nullptr)) { ok=false; break; }
                if (!read) break;
                if (BCryptHashData(hash,buffer.data(),read,0)<0) { ok=false; break; }
            }
            unsigned char digest[32]; char text[65];
            if (ok && BCryptFinishHash(hash,digest,32,0)>=0) {
                for (unsigned i=0;i<32;++i) std::snprintf(text+i*2,3,"%02X",digest[i]);
                ok=std::strcmp(text,HostHash)==0;
            } else ok=false;
        }
    }
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm,0);
    CloseHandle(file); return ok;
}
static bool Bytes(uintptr_t image, size_t offset, std::initializer_list<unsigned char> expected) {
    unsigned char actual[32];
    return expected.size()<=sizeof(actual) && Read(image+offset,actual,expected.size()) &&
        std::memcmp(actual,expected.begin(),expected.size())==0;
}
static Json Snapshot(const std::string& session) {
    const bool ready=live.load() && external::Ready();
    // Resolved COFF symbol `image`. Always use the actual module base.
    const auto image=Field<uintptr_t>(host,runtime::At(runtime::Image));
    const bool guest=ready || (image &&
        Bytes(image,0x1336da0,{0x55,0x48,0x89,0xe5,0x48,0x8d,0x05,0x1d,0x43,0x20,0x04,0x48,0x83,0x38,0x00,0x75}) &&
        Bytes(image,0x2034770,{0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,0x41,0x54,0x53,0x48,0x83,0xec,0x38}));
    const auto world=guest ? Field<uintptr_t>(image,0x553e878) : 0;
    const auto player=Field<uintptr_t>(world,0x60);
    const auto sessionManager=guest ? Field<uintptr_t>(image,0x5540290) : 0;
    const auto data=guest ? Field<uintptr_t>(image,0x553b130) : 0;
    const auto record=Field<uintptr_t>(data,8);
    const auto helper=guest ? Field<uintptr_t>(image,0x553e8b8) : 0;
    const bool frame=Field<unsigned char>(helper,0x50) || Field<unsigned char>(helper,0x51);
    const bool loading=!guest || (Field<unsigned char>(image,0x556286b) && !frame);
    Json state={{"ok",true},{"protocol",1},{"pid",GetCurrentProcessId()},{"session",session},{"source",source},
        {"adapter",Adapter},{"hostSha256",HostHash},{"guestVerified",guest},{"capture",ready},{"restore",ready},
        {"inputBarrier",ready},{"world",bool(world && player && sessionManager)},{"loading",loading},
        {"title",false},{"echoes",Field<int>(record,0x94,-1)},{"level",Field<int>(record,0x90,-1)},
        {"stage",ready ? "experimental-live" : live.load() ? "startup" : "observer"},{"reason",activationError}};
    if (ready) {
        const auto latest=external::Snapshot();
        state["world"]=latest.world; state["loading"]=latest.loading; state["title"]=latest.title;
        state["echoes"]=latest.echoes; state["level"]=latest.level; state["vials"]=latest.vials;
        state["bullets"]=latest.bullets; state["saveHandles"]=latest.handles; state["ioEpoch"]=latest.epoch; state["saveWrites"]=latest.saveWrites;
    }
    return state;
}
static PSECURITY_DESCRIPTOR Security() {
    HANDLE token=nullptr;
    if (!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return nullptr;
    DWORD size=0; GetTokenInformation(token,TokenUser,nullptr,0,&size);
    std::vector<unsigned char> data(size);
    bool ok=GetTokenInformation(token,TokenUser,data.data(),size,&size)!=0;
    CloseHandle(token); if (!ok) return nullptr;
    LPSTR sid=nullptr;
    if (!ConvertSidToStringSidA(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid,&sid)) return nullptr;
    const std::string sddl="D:P(A;;GA;;;SY)(A;;GA;;;"+std::string(sid)+")"; LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor=nullptr;
    ConvertStringSecurityDescriptorToSecurityDescriptorA(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr); return descriptor;
}
static std::string Guid() {
    GUID id{}; CoCreateGuid(&id); wchar_t text[40]; StringFromGUID2(id,text,40);
    auto value=Utf8(text); return value.substr(1,value.size()-2);
}
static bool Receive(HANDLE pipe,std::string& line,ULONGLONG deadline) {
    line.clear();
    while (GetTickCount64()<deadline) {
        DWORD available=0;
        if (!PeekNamedPipe(pipe,nullptr,0,nullptr,&available,nullptr)) return false;
        if (!available) { Sleep(5); continue; }
        char c; DWORD read=0;
        if (!ReadFile(pipe,&c,1,&read,nullptr) || read!=1) return false;
        if (c=='\n') return true;
        if (line.size()==8192) return false;
        line+=c;
    }
    return false;
}
static DWORD WINAPI Server(void*) {
    // Attaching after guest execution begins is read-only. Activation before that
    // boundary covers handles and operations from their creation, without a blind drain delay.
    auto limit=GetTickCount64()+20000;
    // gpu_enabled is set only after the image has been completely read and its file closed.
    while (GetTickCount64()<limit && !Field<int>(host,runtime::At(runtime::Gpu))) Sleep(10);
    wchar_t online[16]{}, party[16]{};
    GetEnvironmentVariableW(L"BB_ONLINE",online,16); GetEnvironmentVariableW(L"BB_PARTY",party,16);
    if (online[0]==L'1' || (party[0] && std::wcscmp(party,L"0"))) activationError="External checkpoints currently support single-player only";
    else live=external::Install(host,activationError);
    auto descriptor=Security(); if (!descriptor) return 2;
    SECURITY_ATTRIBUTES security{sizeof(security),descriptor,FALSE};
    const auto name="\\\\.\\pipe\\bcm-checkpoint-v1-"+std::to_string(GetCurrentProcessId());
    for (;;) {
        HANDLE pipe=CreateNamedPipeA(name.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,16384,16384,0,&security);
        if (pipe==INVALID_HANDLE_VALUE) break;
        if (!ConnectNamedPipe(pipe,nullptr) && GetLastError()!=ERROR_PIPE_CONNECTED) { CloseHandle(pipe); continue; }
        const auto session=Guid(); std::string line,lease; bool mutation=false;
        auto deadline=GetTickCount64()+15000;
        while (Receive(pipe,line,deadline)) {
            Json reply;
            try {
                const auto request=Json::parse(line);
                const auto op=request.value("op",std::string());
                if (op=="hello") reply=Snapshot(session);
                else {
                    if (!live.load() || !external::Ready()) throw std::runtime_error("External adapter is in observation mode; restart the game with the external watcher active.");
                    if (request.value("session",std::string())!=session) throw std::runtime_error("Wrong session");
                    if (op=="acquire") {
                        if (!lease.empty() || external::Frozen()) throw std::runtime_error("Lease already held or recovery required");
                        auto mode=request.value("mode",std::string());
                        if (mode!="capture" && mode!="restore") throw std::runtime_error("Unknown operation");
                        const auto next=Guid();
                        if (!external::Acquire(mode=="restore")) throw std::runtime_error("Safe game access could not be acquired; no saves were replaced.");
                        lease=next;
                        reply={{"ok",true},{"lease",lease},{"source",source},{"frozen",true},
                            {"title",external::Snapshot().title},{"inputBlocked",external::InputBlocked()}};
                    } else {
                        if (lease.empty() || request.value("lease",std::string())!=lease || !external::Frozen())
                            throw std::runtime_error("No valid frozen lease");
                        if (op=="begin-mutation") mutation=true;
                        else if (op=="release") { external::Release(); lease.clear(); mutation=false; }
                        else if (op!="check") throw std::runtime_error("Unknown request");
                        reply={{"ok",true},{"frozen",external::Frozen()}};
                    }
                }
            } catch (const std::exception& error) { reply={{"ok",false},{"error",error.what()}}; }
            deadline=GetTickCount64()+(mutation ? 86400000 : 15000);
            const auto text=reply.dump()+"\n"; DWORD written=0;
            if (!WriteFile(pipe,text.data(),DWORD(text.size()),&written,nullptr) || written!=text.size()) break;
        }
        if (!lease.empty() && !mutation) external::Release();
        DisconnectNamedPipe(pipe); CloseHandle(pipe);
    }
    LocalFree(descriptor); return 0;
}
extern "C" __declspec(dllexport) DWORD WINAPI BcmStart(void* payload) {
    struct StartGuard { StartGuard() { AcquireSRWLockExclusive(&startLock); } ~StartGuard() { ReleaseSRWLockExclusive(&startLock); } } guard;
    try {
        StartConfig config{};
        if (!Read(reinterpret_cast<uintptr_t>(payload),&config,sizeof(config)) ||
            config.size!=sizeof(config) || config.protocol!=3 || config.source[1023]!=0 || config.hash[64]!=0) return 10;
        if (started.load()) return 0;
        host=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
        std::memcpy(HostHash,config.hash,65);
        if (!HostVerified()) return 11;
        const auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(host);
        const auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(host+dos->e_lfanew);
        for (unsigned n=0;n<26;++n) {
            if (n==runtime::FileCount || n==runtime::SaveCount || n==runtime::PadCount) {
                if (!config.layout[n] || config.layout[n]>256) return 17;
            } else if (n==runtime::FileStride || n==runtime::FilePathOffset) {
                const auto stride=config.layout[runtime::FileStride], path=config.layout[runtime::FilePathOffset];
                if (!((stride==0x218 && path==24) || (stride==0x230 && path==32))) return 17;
            } else if (!config.layout[n] || config.layout[n]>=nt->OptionalHeader.SizeOfImage) return 17;
            runtime::layout[n]=config.layout[n];
        }
        // Read the resolved runtime's own user root; do not call guest code.
        char root[512];
        if (!Read(host+runtime::At(runtime::Root),root,sizeof(root)) || !std::memchr(root,0,sizeof(root)) || !root[0]) return 12;
        int length=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,root,-1,nullptr,0);
        if (!length) return 12;
        std::wstring user(length,L'\0'); MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,root,-1,user.data(),length);
        user.pop_back(); user+=L"\\savedata";
        wchar_t resolved[32768], expected[32768];
        if (!GetFullPathNameW(user.c_str(),32768,resolved,nullptr) ||
            !GetFullPathNameW(config.source,32768,expected,nullptr) || _wcsicmp(resolved,expected)!=0) return 13;
        source=Utf8(resolved);
        HMODULE own=nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&BcmStart),&own)) return 14;
        bool no=false; if (!started.compare_exchange_strong(no,true)) return 0;
        HANDLE thread=CreateThread(nullptr,0,Server,nullptr,0,nullptr);
        if (!thread) { started=false; return 15; }
        CloseHandle(thread); return 0;
    } catch (...) { return 16; }
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    if (reason==DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    return TRUE;
}

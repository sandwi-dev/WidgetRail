#pragma once
#include <Windows.h>
#include <cstdint>
#include <vector>
#include <string>

namespace widgetrail::capture::worker {
inline HRESULT Error(){const auto code=GetLastError();return HRESULT_FROM_WIN32(code?code:ERROR_GEN_FAILURE);}
inline void Check(BOOL value){if(!value)throw Error();}
struct Handle {
    HANDLE value{};
    Handle()=default;explicit Handle(HANDLE h):value(h){}
    ~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}
    Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;
};
struct Attributes {
    std::vector<uint8_t> storage;
    LPPROC_THREAD_ATTRIBUTE_LIST list{};
    Attributes(HANDLE& job,std::vector<HANDLE>& inherited){
        SIZE_T bytes{};InitializeProcThreadAttributeList(nullptr,2,0,&bytes);
        if(!bytes||bytes>65536)throw E_FAIL;
        storage.resize(bytes);list=reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
        Check(InitializeProcThreadAttributeList(list,2,0,&bytes));
        try {
            Check(UpdateProcThreadAttribute(list,0,PROC_THREAD_ATTRIBUTE_JOB_LIST,&job,sizeof(job),nullptr,nullptr));
            Check(UpdateProcThreadAttribute(list,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,inherited.data(),inherited.size()*sizeof(HANDLE),nullptr,nullptr));
        }catch(...){DeleteProcThreadAttributeList(list);list=nullptr;throw;}
    }
    ~Attributes(){if(list)DeleteProcThreadAttributeList(list);}
};
inline void Inheritable(HANDLE source,Handle& target){Check(DuplicateHandle(GetCurrentProcess(),source,GetCurrentProcess(),&target.value,0,TRUE,DUPLICATE_SAME_ACCESS));}
inline std::wstring Hex(HANDLE handle){wchar_t text[32]{};swprintf_s(text,L"%llx",static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(handle)));return text;}
inline void CreateJob(Handle& job) {
    job.value=CreateJobObjectW(nullptr,nullptr);if(!job.value)throw Error();
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE|JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION|JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
    limits.BasicLimitInformation.ActiveProcessLimit=1;
    Check(SetInformationJobObject(job.value,JobObjectExtendedLimitInformation,&limits,sizeof(limits)));
}
inline void Launch(const wchar_t* executable,const std::wstring& arguments,std::vector<HANDLE> inherited,
    Handle& job,Handle& process,DWORD& processId) {
    if(!executable||wcschr(executable,L'"'))throw E_INVALIDARG;
    Attributes attributes(job.value,inherited);
    STARTUPINFOEXW startup{};startup.StartupInfo.cb=sizeof(startup);startup.lpAttributeList=attributes.list;
    auto command=L"\""+std::wstring(executable)+L"\" "+arguments;
    PROCESS_INFORMATION created{};
    Check(CreateProcessW(executable,command.data(),nullptr,nullptr,TRUE,
        CREATE_SUSPENDED|CREATE_NO_WINDOW|EXTENDED_STARTUPINFO_PRESENT,nullptr,nullptr,&startup.StartupInfo,&created));
    Handle thread(created.hThread);process.value=created.hProcess;processId=created.dwProcessId;
    BOOL inJob{};Check(IsProcessInJob(process.value,job.value,&inJob));if(!inJob)throw E_ACCESSDENIED;
    if(ResumeThread(thread.value)==DWORD(-1))throw Error();
}
}

#include <windows.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Media.Core.h>
#include <winrt/Windows.Media.Playback.h>
#include <atomic>
#include <cstdio>
#include <filesystem>
#include <stdexcept>
using namespace winrt::Windows::Media::Core;
using namespace winrt::Windows::Media::Playback;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);printf("PASS %s\n",message);fflush(stdout);}
struct Event {HANDLE value{CreateEventW(nullptr,TRUE,FALSE,nullptr)};~Event(){if(value)CloseHandle(value);}};
int wmain(int argc,wchar_t** argv) {
    if(argc!=3)return 2;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        const std::filesystem::path directory(argv[2]);
        Check(!std::filesystem::exists(directory),"fresh owned media-move fixture directory");std::filesystem::create_directories(directory);
        const auto original=directory/L"staged.mp4",saved=directory/L"saved.mp4";
        Check(CopyFileW(argv[1],original.c_str(),TRUE)!=0,"small owned fixture copied for destructive rename test only");
        MediaPlayer player;player.AutoPlay(false);player.IsMuted(true);player.IsLoopingEnabled(true);
        player.CommandManager().IsEnabled(false);player.SystemMediaTransportControls().IsEnabled(false);
        Event opened,failed;std::atomic<int32_t> error{};
        auto openSubscription=player.MediaOpened(winrt::auto_revoke,[&](auto&&,auto&&){SetEvent(opened.value);});
        auto errorSubscription=player.MediaFailed(winrt::auto_revoke,[&](auto&&,MediaPlayerFailedEventArgs const& args){error=args.ExtendedErrorCode().value;SetEvent(failed.value);});
        const winrt::Windows::Foundation::Uri uri(L"file:///"+original.generic_wstring());
        auto source=MediaSource::CreateFromUri(uri);player.Source(source);
        HANDLE events[]{opened.value,failed.value};
        const auto opening=WaitForMultipleObjects(2,events,FALSE,10000);
        Check(opening==WAIT_OBJECT_0,"actual Windows MediaPlayer opened URI-backed staged MP4");
        player.Play();const auto deadline=GetTickCount64()+5000;
        while((player.PlaybackSession().PlaybackState()!=MediaPlaybackState::Playing||player.PlaybackSession().Position().count()<500000)&&GetTickCount64()<deadline)Sleep(10);
        Check(player.PlaybackSession().PlaybackState()==MediaPlaybackState::Playing&&player.PlaybackSession().Position().count()>=500000,
            "native playback is advancing before same-volume rename");
        const auto before=player.PlaybackSession().Position().count();
        const auto moved=MoveFileExW(original.c_str(),saved.c_str(),MOVEFILE_WRITE_THROUGH)!=0;
        const auto moveError=moved?ERROR_SUCCESS:GetLastError();
        Sleep(180);const auto after=player.PlaybackSession().Position().count();
        const auto state=player.PlaybackSession().PlaybackState();
        printf("moveDuringPlayback=%s win32=%lu hresult=%08x before100ns=%lld after100ns=%lld playbackState=%u mediaError=%08x\n",
            moved?"true":"false",moveError,static_cast<uint32_t>(HRESULT_FROM_WIN32(moveError)),before,after,static_cast<unsigned>(state),static_cast<uint32_t>(error.load()));
        Check(state==MediaPlaybackState::Playing&&before!=after&&error.load()==0,"playback keeps advancing after the rename attempt");
        player.Pause();player.Source(nullptr);player.Close();source.Close();
        errorSubscription.revoke();openSubscription.revoke();
        bool afterClose=moved;
        if(!moved)afterClose=MoveFileExW(original.c_str(),saved.c_str(),MOVEFILE_WRITE_THROUGH)!=0;
        const auto closeMoveError=afterClose?ERROR_SUCCESS:GetLastError();
        printf("moveAfterClose=%s win32=%lu originalExists=%s savedExists=%s bytes=%llu\n",afterClose?"true":"false",closeMoveError,
            std::filesystem::exists(original)?"true":"false",std::filesystem::exists(saved)?"true":"false",
            static_cast<unsigned long long>(std::filesystem::exists(saved)?std::filesystem::file_size(saved):0));
        Check(afterClose&&!std::filesystem::exists(original)&&std::filesystem::exists(saved),"closing native player/source releases file for same-volume publication");
        return 0;
    }catch(const winrt::hresult_error& error){printf("FAIL MediaPlayer HRESULT=%08x\n",static_cast<uint32_t>(error.code().value));return 1;}
     catch(const std::exception& error){printf("FAIL MediaPlayer %s\n",error.what());return 1;}
}

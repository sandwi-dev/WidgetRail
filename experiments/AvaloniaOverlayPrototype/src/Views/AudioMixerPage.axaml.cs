using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GameBarAlternative.AvaloniaPrototype.ViewModels;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class AudioMixerPage : UserControl, IPrototypeFocusPage
{
    public AudioMixerPage() : this(new AudioMixerPageViewModel())
    {
    }

    internal AudioMixerPage(AudioMixerPageViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        var master = this.FindControl<Slider>("MasterVolumeSlider")!;
        var game = this.FindControl<Slider>("GameVolumeSlider")!;
        var spotify = this.FindControl<Slider>("SpotifyVolumeSlider")!;
        var voice = this.FindControl<Slider>("VoiceVolumeSlider")!;
        var masterMute = this.FindControl<Button>("PrimaryAction")!;
        var gameMute = this.FindControl<Button>("GameMuteButton")!;
        var spotifyMute = this.FindControl<Button>("SpotifyMuteButton")!;
        var voiceMute = this.FindControl<Button>("VoiceMuteButton")!;
        master.SetValue(XYFocus.DownProperty, game);
        game.SetValue(XYFocus.UpProperty, master);
        game.SetValue(XYFocus.DownProperty, spotify);
        spotify.SetValue(XYFocus.UpProperty, game);
        spotify.SetValue(XYFocus.DownProperty, voice);
        voice.SetValue(XYFocus.UpProperty, spotify);
        voice.SetValue(XYFocus.DownProperty, voiceMute);
        voiceMute.SetValue(XYFocus.LeftProperty, voice);
        masterMute.SetValue(XYFocus.DownProperty, gameMute);
        gameMute.SetValue(XYFocus.UpProperty, masterMute);
        gameMute.SetValue(XYFocus.DownProperty, spotifyMute);
        spotifyMute.SetValue(XYFocus.UpProperty, gameMute);
        spotifyMute.SetValue(XYFocus.DownProperty, voiceMute);
        voiceMute.SetValue(XYFocus.UpProperty, spotifyMute);
    }

    public Control InitialFocus => this.FindControl<Slider>("MasterVolumeSlider")!;
}

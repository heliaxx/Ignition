using Godot;
using System;

public partial class Audio : Control
{
    private Slider generalVolumeSlider;
    private Slider musicVolumeSlider;
    private Slider sfxVolumeSlider;
    private Label generalVolumeValue;
    private Label musicVolumeValue;
    private Label sfxVolumeValue;

    public override void _Ready()
    {
        generalVolumeSlider = GetNode<Slider>("%VolGeneralSlider");
        musicVolumeSlider = GetNode<Slider>("%VolMusicSlider");
        sfxVolumeSlider = GetNode<Slider>("%VolSFXSlider");
        generalVolumeValue = GetNode<Label>("%VolGeneralValue");
        musicVolumeValue = GetNode<Label>("%VolMusicValue");
        sfxVolumeValue = GetNode<Label>("%VolSFXValue");

        LoadSettingsIntoUi();

        generalVolumeSlider.ValueChanged += value => OnVolumeChanged("general_volume", value);
        musicVolumeSlider.ValueChanged += value => OnVolumeChanged("music_volume", value);
        sfxVolumeSlider.ValueChanged += value => OnVolumeChanged("sfx_volume", value);

        MenuUtils.AttachButtonSounds(this);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("menu"))
        {
            GetViewport().SetInputAsHandled();
            _on_back_btn_pressed();
        }
    }

    private void LoadSettingsIntoUi()
    {
        var audioSettings = ConfigFileHandler.Instance.LoadAudioSettings();

        if (audioSettings.TryGetValue("general_volume", out Variant generalVol))
            generalVolumeSlider.Value = Math.Min(generalVol.As<float>(), 1.0f) * 100.0f;
        if (audioSettings.TryGetValue("music_volume", out Variant musicVol))
            musicVolumeSlider.Value = Math.Min(musicVol.As<float>(), 1.0f) * 100.0f;
        if (audioSettings.TryGetValue("sfx_volume", out Variant sfxVol))
            sfxVolumeSlider.Value = Math.Min(sfxVol.As<float>(), 1.0f) * 100.0f;
        ShowVolumes();
    }

    private void ShowVolumes()
    {
        generalVolumeValue.Text = $"{generalVolumeSlider.Value:0}%";
        musicVolumeValue.Text = $"{musicVolumeSlider.Value:0}%";
        sfxVolumeValue.Text = $"{sfxVolumeSlider.Value:0}%";
    }

    private void OnVolumeChanged(string key, double sliderValue)
    {
        ConfigFileHandler.Instance.SaveAudioSettings(key, (float)(sliderValue / 100.0));
        EventBus.EmitAudioSettingsChanged();
        ShowVolumes();
    }

    private void _on_back_btn_pressed()
    {
        GetParent<OptionsMenu>().Close();
    }

    private void _on_reset_btn_pressed()
    {
        ConfigFileHandler.Instance.ResetAudioSettings();
        LoadSettingsIntoUi();
        EventBus.EmitAudioSettingsChanged();
    }
}

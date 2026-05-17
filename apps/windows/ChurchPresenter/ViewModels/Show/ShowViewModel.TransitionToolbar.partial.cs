using System;
using System.Globalization;
using System.Threading.Tasks;


using CommunityToolkit.Mvvm.ComponentModel;

namespace ChurchPresenter.ViewModels;

/// <summary>Show toolbar: persisted global slide/media transitions (cut / dissolve / custom).</summary>
public partial class ShowViewModel
{
    private ShowToolbarTransitionDto _slideToolbarDto = new();
    private ShowToolbarTransitionDto _mediaToolbarDto = new();

    /// <summary>-1 = unset, 0 = cut, 1 = dissolve, 2 = custom (slide).</summary>
    [ObservableProperty]
    private int _globalSlideTransitionModeIndex = -1;

    [ObservableProperty]
    private int _globalSlideDissolveDurationMs = 200;

    /// <summary>-1 = unset, 0 = cut, 1 = dissolve, 2 = custom (media).</summary>
    [ObservableProperty]
    private int _globalMediaTransitionModeIndex = -1;

    [ObservableProperty]
    private int _globalMediaDissolveDurationMs = 200;

    public bool GlobalSlideTransitionIsCut => GlobalSlideTransitionModeIndex == 0;

    public bool GlobalSlideTransitionIsDissolve => GlobalSlideTransitionModeIndex == 1;

    public bool GlobalSlideTransitionIsCustom => GlobalSlideTransitionModeIndex == 2;

    public bool GlobalMediaTransitionIsCut => GlobalMediaTransitionModeIndex == 0;

    public bool GlobalMediaTransitionIsDissolve => GlobalMediaTransitionModeIndex == 1;

    public bool GlobalMediaTransitionIsCustom => GlobalMediaTransitionModeIndex == 2;

    /// <summary>Catalog name, optional direction, and duration for the slide custom mode toolbar.</summary>
    public string GlobalSlideCustomTransitionToolbarLabel => FormatToolbarCustomLabel(_slideToolbarDto);

    /// <summary>Catalog name, optional direction, and duration for the media custom mode toolbar.</summary>
    public string GlobalMediaCustomTransitionToolbarLabel => FormatToolbarCustomLabel(_mediaToolbarDto);

    private static string FormatToolbarCustomLabel(ShowToolbarTransitionDto dto)
    {
        if (dto.Custom == null)
            return "Custom";

        var name = MediaCueTransitionFormatter.FormatLabel(dto.Custom);
        var sec = (dto.Custom.Duration / 1000.0).ToString("0.###", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(name) ? $"{sec} s" : $"{name} · {sec} s";
    }

    partial void OnGlobalSlideTransitionModeIndexChanged(int value)
    {
        _slideToolbarDto.Mode = ModeFromSlideIndex(value);
        NotifyGlobalSlideToolbarLabels();
        if (!_loadingDeckPreferences)
        {
            PushGlobalTransitionsToEngine();
            _ = PersistDeckPreferencesAsync();
            NotifyPreviewState();
        }
    }

    partial void OnGlobalSlideDissolveDurationMsChanged(int value)
    {
        _slideToolbarDto.DissolveDurationMs = Math.Clamp(value, 50, 10_000);
        if (GlobalSlideTransitionModeIndex == 1)
            _slideToolbarDto.Mode = "dissolve";
        if (!_loadingDeckPreferences)
        {
            PushGlobalTransitionsToEngine();
            _ = PersistDeckPreferencesAsync();
            NotifyPreviewState();
        }
    }

    partial void OnGlobalMediaTransitionModeIndexChanged(int value)
    {
        _mediaToolbarDto.Mode = ModeFromSlideIndex(value);
        NotifyGlobalMediaToolbarLabels();
        if (!_loadingDeckPreferences)
        {
            PushGlobalTransitionsToEngine();
            _ = PersistDeckPreferencesAsync();
            NotifyPreviewState();
        }
    }

    partial void OnGlobalMediaDissolveDurationMsChanged(int value)
    {
        _mediaToolbarDto.DissolveDurationMs = Math.Clamp(value, 50, 10_000);
        if (GlobalMediaTransitionModeIndex == 1)
            _mediaToolbarDto.Mode = "dissolve";
        if (!_loadingDeckPreferences)
        {
            PushGlobalTransitionsToEngine();
            _ = PersistDeckPreferencesAsync();
            NotifyPreviewState();
        }
    }

    private void NotifyGlobalSlideToolbarLabels()
    {
        OnPropertyChanged(nameof(GlobalSlideTransitionIsCut));
        OnPropertyChanged(nameof(GlobalSlideTransitionIsDissolve));
        OnPropertyChanged(nameof(GlobalSlideTransitionIsCustom));
        OnPropertyChanged(nameof(GlobalSlideCustomTransitionToolbarLabel));
    }

    private void NotifyGlobalMediaToolbarLabels()
    {
        OnPropertyChanged(nameof(GlobalMediaTransitionIsCut));
        OnPropertyChanged(nameof(GlobalMediaTransitionIsDissolve));
        OnPropertyChanged(nameof(GlobalMediaTransitionIsCustom));
        OnPropertyChanged(nameof(GlobalMediaCustomTransitionToolbarLabel));
    }

    private void LoadTransitionToolbarFromSettings(ShowSettingsDto show)
    {
        _slideToolbarDto = CloneToolbarDto(show.GlobalSlideTransition);
        _mediaToolbarDto = CloneToolbarDto(show.GlobalMediaTransition);

        GlobalSlideTransitionModeIndex = IndexFromMode(_slideToolbarDto.Mode);
        GlobalSlideDissolveDurationMs = Math.Clamp(_slideToolbarDto.DissolveDurationMs, 50, 10_000);
        GlobalMediaTransitionModeIndex = IndexFromMode(_mediaToolbarDto.Mode);
        GlobalMediaDissolveDurationMs = Math.Clamp(_mediaToolbarDto.DissolveDurationMs, 50, 10_000);

        PushGlobalTransitionsToEngine();
        NotifyGlobalSlideToolbarLabels();
        NotifyGlobalMediaToolbarLabels();
    }

    private void PersistTransitionToolbarToSettings(AppSettingsDto s)
    {
        s.Show.GlobalSlideTransition = CloneToolbarDto(_slideToolbarDto);
        s.Show.GlobalMediaTransition = CloneToolbarDto(_mediaToolbarDto);
    }

    private void PushGlobalTransitionsToEngine()
    {
        _transitionDefaults.SetGlobalSlideFallback(ShowTransitionToolbar.ToSlideTransition(_slideToolbarDto));
        _transitionDefaults.SetGlobalMediaFallback(ShowTransitionToolbar.ToSlideTransition(_mediaToolbarDto));
        _engine.NotifyGlobalTransitionDefaultsChanged();
    }

    /// <summary>Applies a catalog transition as the global slide “custom” mode and persists.</summary>
    public Task ApplyGlobalSlideCustomTransitionAsync(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var normalized = TransitionStorageNormalizer.NormalizeForStorage(transition)
            ?? transition;
        _slideToolbarDto.Custom = PresentationModelUtilities.DeepClone(normalized);
        _slideToolbarDto.Mode = "custom";
        PushGlobalTransitionsToEngine();
        if (GlobalSlideTransitionModeIndex != 2)
            GlobalSlideTransitionModeIndex = 2;
        else
            NotifyPreviewState();
        NotifyGlobalSlideToolbarLabels();
        return PersistDeckPreferencesAsync();
    }

    /// <summary>Applies a catalog transition as the global media “custom” mode and persists.</summary>
    public Task ApplyGlobalMediaCustomTransitionAsync(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var normalized = TransitionStorageNormalizer.NormalizeForStorage(transition)
            ?? transition;
        _mediaToolbarDto.Custom = PresentationModelUtilities.DeepClone(normalized);
        _mediaToolbarDto.Mode = "custom";
        PushGlobalTransitionsToEngine();
        if (GlobalMediaTransitionModeIndex != 2)
            GlobalMediaTransitionModeIndex = 2;
        else
            NotifyPreviewState();
        NotifyGlobalMediaToolbarLabels();
        return PersistDeckPreferencesAsync();
    }

    /// <summary>Clears the global slide toolbar transition so no fallback transition is applied.</summary>
    public Task ResetGlobalSlideCustomTransitionAsync()
    {
        _slideToolbarDto.Mode = string.Empty;
        _slideToolbarDto.Custom = null;
        _slideToolbarDto.DissolveDurationMs = GlobalSlideDissolveDurationMs;
        if (GlobalSlideTransitionModeIndex != -1)
            GlobalSlideTransitionModeIndex = -1;
        else
        {
            PushGlobalTransitionsToEngine();
            NotifyPreviewState();
        }
        NotifyGlobalSlideToolbarLabels();
        return PersistDeckPreferencesAsync();
    }

    /// <summary>Clears the global media toolbar transition so no fallback transition is applied.</summary>
    public Task ResetGlobalMediaCustomTransitionAsync()
    {
        _mediaToolbarDto.Mode = string.Empty;
        _mediaToolbarDto.Custom = null;
        _mediaToolbarDto.DissolveDurationMs = GlobalMediaDissolveDurationMs;
        if (GlobalMediaTransitionModeIndex != -1)
            GlobalMediaTransitionModeIndex = -1;
        else
        {
            PushGlobalTransitionsToEngine();
            NotifyPreviewState();
        }
        NotifyGlobalMediaToolbarLabels();
        return PersistDeckPreferencesAsync();
    }

    /// <summary>Slide transition to pre-fill the custom picker (when in custom mode).</summary>
    public SlideTransition? GetGlobalSlideCustomTransitionForPicker() =>
        _slideToolbarDto.Custom == null
            ? null
            : PresentationModelUtilities.DeepClone(_slideToolbarDto.Custom);

    /// <summary>Media transition to pre-fill the custom picker (when in custom mode).</summary>
    public SlideTransition? GetGlobalMediaCustomTransitionForPicker() =>
        _mediaToolbarDto.Custom == null
            ? null
            : PresentationModelUtilities.DeepClone(_mediaToolbarDto.Custom);

    private static int IndexFromMode(string? mode)
    {
        var m = (mode ?? "").Trim().ToLowerInvariant();
        return m switch
        {
            "cut" => 0,
            "dissolve" => 1,
            "custom" => 2,
            _ => -1,
        };
    }

    private static string ModeFromSlideIndex(int index) =>
        index switch
        {
            0 => "cut",
            1 => "dissolve",
            2 => "custom",
            _ => string.Empty,
        };

    private static ShowToolbarTransitionDto CloneToolbarDto(ShowToolbarTransitionDto? source)
    {
        source ??= new ShowToolbarTransitionDto();
        return new ShowToolbarTransitionDto
        {
            Mode = string.IsNullOrWhiteSpace(source.Mode) ? string.Empty : source.Mode,
            DissolveDurationMs = source.DissolveDurationMs <= 0 ? 200 : source.DissolveDurationMs,
            Custom = source.Custom == null ? null : PresentationModelUtilities.DeepClone(source.Custom),
        };
    }
}

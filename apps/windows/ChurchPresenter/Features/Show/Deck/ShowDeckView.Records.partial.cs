using System.Collections.Generic;

using ChurchPresenter.Models.Media;
using ChurchPresenter.Models.Show;

using Microsoft.UI.Xaml.Media;

namespace ChurchPresenter.Features.Show.Deck;

public sealed partial class ShowDeckView
{
    private sealed record ThemeSelection(ThemeTemplate Theme, ThemeTemplateSlide Slide);

    private sealed record ThemeChoice(ThemeTemplate Theme, string Name);

    private sealed record ThemeSlideChoice(ThemeTemplateSlide Slide, string Name);

    private sealed record ThemeMenuChoice(ThemeTemplate Theme, string DisplayName, string? Folder, bool IsEmbedded);

    private sealed record SlideGroupMenuChoice(string Label, Brush Brush);

    private sealed record TransitionDialogResult(bool Submitted, bool ClearRequested, SlideTransition? Transition);

    private sealed record SelectionDialogResult(bool Submitted, bool ClearRequested, string? Value);

    private sealed record MediaConfigurationResult(SlideBackground? Background, List<SlideMediaCue> Cues);

    private sealed record TimerChoice(ShowTimerDefinition Timer)
    {
        public string Name => $"{Timer.Name} ({Timer.DurationSeconds}s)";
    }

    private sealed record ActionChoice(SlideActionDefinition Action)
    {
        public string Name => GetSlideActionLabel(Action);
    }

    private sealed record MediaChoice(MediaEntry Entry)
    {
        public string Name => string.IsNullOrWhiteSpace(Entry.FileName)
            ? $"{Entry.Id} ({Entry.Type})"
            : $"{Entry.FileName} ({Entry.Type})";
    }

    private sealed record CueChoice(SlideMediaCue Cue, string Name);
}

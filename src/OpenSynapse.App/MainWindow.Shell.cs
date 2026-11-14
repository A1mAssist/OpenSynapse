using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace OpenSynapse.App;

/// <summary>
/// Launch overlay animation. Every frame runs on the compositor so the flight stays
/// smooth while device enumeration and battery polling are still posting UI work.
/// </summary>
public sealed partial class MainWindow
{
    private const double LaunchFlightDuration = 1350;
    private const double LaunchCaptionDuration = 210;
    private const double LaunchSpinDuration = 1140;
    private const double LaunchTitleDuration = 1050;
    private const double LaunchTitleDelay = 60;
    private const float WordmarkGap = 8f;

    private static readonly UISettings s_uiSettings = new();

    private void HideLaunchOverlay()
    {
        LaunchProgressRing.IsActive = false;
        if (s_uiSettings.AnimationsEnabled)
        {
            _ = PlayLaunchExitAsync();
        }
        else
        {
            FinishLaunchOverlay();
        }
    }

    private async Task PlayLaunchExitAsync()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(LaunchContent).Compositor;

        FadeOut(compositor, LaunchTitleText, LaunchCaptionDuration, Ease(compositor, 0.42f, 0f, 0.18f, 1f));
        FadeOut(compositor, LaunchSubtitleText, LaunchCaptionDuration, Ease(compositor, 0.42f, 0f, 0.18f, 1f));
        FadeOut(compositor, LaunchStatusText, LaunchCaptionDuration, Ease(compositor, 0.42f, 0f, 0.18f, 1f));
        FadeOut(compositor, LaunchVeil, LaunchFlightDuration, Ease(compositor, 0.65f, 0f, 0.35f, 1f));
        FadeOut(compositor, LaunchBackdrop, LaunchFlightDuration, Ease(compositor, 0.65f, 0f, 0.35f, 1f));

        await PlayMarkFlightAsync(compositor);

        LaunchContent.Opacity = 0;
        TitleBarLogo.Opacity = 1;
        TitleBarTitleText.Opacity = 1;
        TitleBarSubtitleText.Opacity = 1;

        PlayBrandArrival(compositor);
        await Task.Delay(TimeSpan.FromMilliseconds(LaunchSpinDuration));
        FinishLaunchOverlay();
    }

    private async Task PlayMarkFlightAsync(Compositor compositor)
    {
        var start = LaunchMarkImage
            .TransformToVisual(RootLayout)
            .TransformBounds(new Rect(0, 0, LaunchMarkImage.ActualWidth, LaunchMarkImage.ActualHeight));
        var end = TitleBarLogo
            .TransformToVisual(RootLayout)
            .TransformBounds(new Rect(0, 0, TitleBarLogo.ActualWidth, TitleBarLogo.ActualHeight));

        var startWidth = Math.Max(1d, start.Width);
        var scale = (float)Math.Max(0.01d, end.Width / startWidth);
        var dx = (float)(end.X + end.Width / 2 - (start.X + start.Width / 2));
        var dy = (float)(end.Y + end.Height / 2 - (start.Y + start.Height / 2));

        // Layout rewrites Offset every pass; Translation is the channel the compositor owns.
        ElementCompositionPreview.SetIsTranslationEnabled(LaunchMarkImage, true);
        var visual = ElementCompositionPreview.GetElementVisual(LaunchMarkImage);
        visual.CenterPoint = new Vector3((float)(startWidth / 2), (float)(start.Height / 2), 0f);

        var glide = compositor.CreateVector3KeyFrameAnimation();
        glide.InsertKeyFrame(1f, new Vector3(dx, dy, 0f), Ease(compositor, 0.42f, 0f, 0.18f, 1f));
        glide.Duration = TimeSpan.FromMilliseconds(LaunchFlightDuration);
        visual.StartAnimation("Translation", glide);

        var sizing = compositor.CreateVector3KeyFrameAnimation();
        sizing.InsertKeyFrame(1f, new Vector3(scale, scale, 1f), Ease(compositor, 0.34f, 1.32f, 0.56f, 1f));
        sizing.Duration = TimeSpan.FromMilliseconds(LaunchFlightDuration);
        visual.StartAnimation("Scale", sizing);

        FadeOut(compositor, LaunchProgressRing, LaunchCaptionDuration, Ease(compositor, 0.42f, 0f, 0.18f, 1f));

        await Task.Delay(TimeSpan.FromMilliseconds(LaunchFlightDuration));
    }

    private void PlayBrandArrival(Compositor compositor)
    {
        var logo = ElementCompositionPreview.GetElementVisual(TitleBarLogo);
        logo.CenterPoint = new Vector3(
            (float)(TitleBarLogo.ActualWidth / 2),
            (float)(TitleBarLogo.ActualHeight / 2),
            0f);
        var spin = compositor.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(0f, 0f);
        spin.InsertKeyFrame(1f, 360f, Ease(compositor, 0.3f, 0f, 0.2f, 1f));
        spin.Duration = TimeSpan.FromMilliseconds(LaunchSpinDuration);
        logo.StartAnimation("RotationAngleInDegrees", spin);

        StartTitleReveal(compositor);
    }

    private void StartTitleReveal(Compositor compositor)
    {
        var view = TitleBarTextStack;
        var clip = (RectangleGeometry)TitleBarTitleClip.Clip;
        clip.Rect = new Rect(0, -4, view.ActualWidth, view.ActualHeight + 8);

        var title = ElementCompositionPreview.GetElementVisual(view);
        ElementCompositionPreview.SetIsTranslationEnabled(view, true);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(-(float)(view.ActualWidth + WordmarkGap), 0f, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, Ease(compositor, 0.1f, 0.9f, 0.2f, 1f));
        slide.Duration = TimeSpan.FromMilliseconds(LaunchTitleDuration);
        slide.DelayTime = TimeSpan.FromMilliseconds(LaunchTitleDelay);
        slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        title.StartAnimation("Translation", slide);
    }

    private static void FadeOut(
        Compositor compositor,
        UIElement element,
        double milliseconds,
        CompositionEasingFunction easing)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, easing);
        fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", fade);
    }

    private static CompositionEasingFunction Ease(
        Compositor compositor,
        float x1,
        float y1,
        float x2,
        float y2) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));

    private void FinishLaunchOverlay()
    {
        ShowTitleBarBrand();
        CollapseLaunchLayers();
        ApplyTitleBarColors();
        StartPendingIntroduction();
    }

    private void ShowTitleBarBrand()
    {
        var logo = ElementCompositionPreview.GetElementVisual(TitleBarLogo);
        StopAnimationSafe(logo, "RotationAngleInDegrees");
        logo.RotationAngleInDegrees = 0f;
        logo.Opacity = 1f;

        TitleBarLogo.Opacity = 1;
        TitleBarTitleText.Opacity = 1;
        TitleBarSubtitleText.Opacity = 1;
        var clip = (RectangleGeometry)TitleBarTitleClip.Clip;
        var titleWidth = TitleBarTextStack.ActualWidth;
        if (titleWidth > 1)
        {
            clip.Rect = new Rect(0, -4, titleWidth, TitleBarTextStack.ActualHeight + 8);
        }

        var title = ElementCompositionPreview.GetElementVisual(TitleBarTextStack);
        StopAnimationSafe(title, "Translation");
    }

    private void CollapseLaunchLayers()
    {
        LaunchContent.Visibility = Visibility.Collapsed;
        LaunchVeil.Visibility = Visibility.Collapsed;
        LaunchBackdrop.Visibility = Visibility.Collapsed;
    }

    private static void StopAnimationSafe(Visual visual, string property)
    {
        try
        {
            visual.StopAnimation(property);
        }
        catch (ArgumentException)
        {
            // StopAnimation throws when the channel never started. Silent-launch
            // paths reach here before any animation has run.
        }
    }
}

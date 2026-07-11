using System;

namespace StrataTheme.Controls;

public enum ChatScrollIntent
{
    PreserveViewport,
    FollowBottom,
    RevealSentMessage,
    InitialBottom,
}

public enum ChatScrollAction
{
    None,
    ScrollToBottom,
    CompensateViewport,
}

public readonly record struct ChatScrollDecision(
    ChatScrollAction Action,
    long Generation,
    double OffsetDelta = 0d)
{
    public static ChatScrollDecision None(long generation) => new(ChatScrollAction.None, generation);
}

public readonly record struct ChatScrollMetrics
{
    public ChatScrollMetrics(double offsetY, double extentHeight, double viewportHeight)
    {
        if (!double.IsFinite(offsetY)
            || !double.IsFinite(extentHeight)
            || !double.IsFinite(viewportHeight))
        {
            throw new ArgumentException("Scroll metrics must be finite.");
        }

        OffsetY = Math.Max(0d, offsetY);
        ExtentHeight = Math.Max(0d, extentHeight);
        ViewportHeight = Math.Max(0d, viewportHeight);
    }

    public double OffsetY { get; }
    public double ExtentHeight { get; }
    public double ViewportHeight { get; }
    public double MaxOffset => Math.Max(0d, ExtentHeight - ViewportHeight);
    public double DistanceFromBottom => Math.Max(0d, MaxOffset - OffsetY);
}

/// <summary>
/// Owns chat transcript scroll intent. UI controls report user input and content changes,
/// then execute the returned generation-stamped decisions.
/// </summary>
public sealed class ChatScrollPolicy
{
    public const double DefaultBottomTolerance = 8d;
    public const double FractionalEpsilon = 0.5d;

    private readonly double _bottomTolerance;
    private bool _hasPendingUnseenContent;

    public ChatScrollPolicy(double bottomTolerance = DefaultBottomTolerance)
    {
        if (!double.IsFinite(bottomTolerance) || bottomTolerance < 0d)
            throw new ArgumentOutOfRangeException(nameof(bottomTolerance));

        _bottomTolerance = bottomTolerance;
    }

    public ChatScrollIntent Intent { get; private set; } = ChatScrollIntent.FollowBottom;
    public long Generation { get; private set; }
    public bool HasUnseenContent { get; private set; }

    public bool IsFollowingTail => Intent is ChatScrollIntent.FollowBottom
        or ChatScrollIntent.RevealSentMessage
        or ChatScrollIntent.InitialBottom;

    public bool IsAtBottom(in ChatScrollMetrics metrics) =>
        metrics.DistanceFromBottom <= _bottomTolerance;

    public ChatScrollDecision RequestInitialBottom()
    {
        _hasPendingUnseenContent = false;
        return Enter(ChatScrollIntent.InitialBottom);
    }

    public ChatScrollDecision RequestRevealSentMessage()
    {
        _hasPendingUnseenContent |= HasUnseenContent;
        return Enter(ChatScrollIntent.RevealSentMessage);
    }

    public void EnterFollowMode()
    {
        _hasPendingUnseenContent |= HasUnseenContent;
        HasUnseenContent = false;
        SetIntent(ChatScrollIntent.FollowBottom, forceGenerationBump: true);
    }

    public void PreserveViewport()
    {
        PromotePendingUnseenContent();
        SetIntent(ChatScrollIntent.PreserveViewport, forceGenerationBump: true);
    }

    public void OnUserInput(bool leavesTail)
    {
        Generation++;
        if (leavesTail)
        {
            Intent = ChatScrollIntent.PreserveViewport;
            PromotePendingUnseenContent();
        }
    }

    public void OnUserScroll(in ChatScrollMetrics metrics, double offsetDeltaY)
    {
        Generation++;

        if (offsetDeltaY < -FractionalEpsilon || !IsAtBottom(in metrics))
        {
            Intent = ChatScrollIntent.PreserveViewport;
            PromotePendingUnseenContent();
            return;
        }

        if (offsetDeltaY > FractionalEpsilon)
        {
            Intent = ChatScrollIntent.FollowBottom;
            HasUnseenContent = false;
            _hasPendingUnseenContent = false;
        }
    }

    public ChatScrollDecision OnContentChanged(in ChatScrollMetrics metrics, bool markAsUnseen)
    {
        if (IsFollowingTail)
        {
            if (markAsUnseen)
                _hasPendingUnseenContent = true;

            return new ChatScrollDecision(ChatScrollAction.ScrollToBottom, Generation);
        }

        if (markAsUnseen)
            HasUnseenContent = true;

        return ChatScrollDecision.None(Generation);
    }

    public ChatScrollDecision OnContentAboveViewportResized(double delta)
    {
        if (IsFollowingTail)
            return new ChatScrollDecision(ChatScrollAction.ScrollToBottom, Generation);

        if (!double.IsFinite(delta) || Math.Abs(delta) < FractionalEpsilon)
            return ChatScrollDecision.None(Generation);

        return new ChatScrollDecision(ChatScrollAction.CompensateViewport, Generation, delta);
    }

    public void OnBottomLanded(in ChatScrollMetrics metrics)
    {
        if (!IsFollowingTail || !IsAtBottom(in metrics))
            return;

        Intent = ChatScrollIntent.FollowBottom;
        HasUnseenContent = false;
        _hasPendingUnseenContent = false;
    }

    private ChatScrollDecision Enter(ChatScrollIntent intent)
    {
        HasUnseenContent = false;
        SetIntent(intent, forceGenerationBump: true);
        return new ChatScrollDecision(ChatScrollAction.ScrollToBottom, Generation);
    }

    private void SetIntent(ChatScrollIntent intent, bool forceGenerationBump)
    {
        if (!forceGenerationBump && Intent == intent)
            return;

        Intent = intent;
        Generation++;
    }

    private void PromotePendingUnseenContent()
    {
        if (!_hasPendingUnseenContent)
            return;

        HasUnseenContent = true;
        _hasPendingUnseenContent = false;
    }
}

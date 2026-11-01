using System.Windows.Input;
using System.Windows.Threading;
using LumaProfiles.Models;

namespace LumaProfiles.ViewModels;

/// <summary>
/// Undo / redo for the adjustment panel (Ctrl+Z / Ctrl+Y). Dragging a slider produces many small changes, so
/// changes that follow each other within <see cref="HistoryPause"/> become a single step.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxHistory = 50;
    private static readonly TimeSpan HistoryPause = TimeSpan.FromMilliseconds(600);

    private readonly List<DisplayProfile> _undo = [];
    private readonly List<DisplayProfile> _redo = [];
    private readonly DispatcherTimer _historyTimer = new() { Interval = HistoryPause };
    private DisplayProfile? _historyBaseline;
    private bool _historyBurst;
    private bool _applyingHistory;

    public ICommand UndoAdjustmentCommand { get; private set; } = null!;
    public ICommand RedoAdjustmentCommand { get; private set; } = null!;

    public bool CanUndoAdjustment => _undo.Count > 0 || _historyBurst;
    public bool CanRedoAdjustment => _redo.Count > 0;

    private void InitializeHistory()
    {
        UndoAdjustmentCommand = new RelayCommand(UndoAdjustment, () => CanUndoAdjustment);
        RedoAdjustmentCommand = new RelayCommand(RedoAdjustment, () => CanRedoAdjustment);
        _historyTimer.Tick += (_, _) => CloseHistoryStep();
        ResetHistory();
    }

    /// <summary>A different profile was selected: its history starts empty.</summary>
    private void ResetHistory()
    {
        _historyTimer.Stop();
        _undo.Clear();
        _redo.Clear();
        _historyBurst = false;
        _historyBaseline = _selectedProfile.Clone();
        RaiseHistory();
    }

    /// <summary>Called for every adjustment change of the selected profile.</summary>
    private void RecordAdjustmentChange()
    {
        if (_applyingHistory || _historyBaseline is null) return;
        if (!_historyBurst)
        {
            Push(_undo, _historyBaseline);
            _redo.Clear();
            _historyBurst = true;
            RaiseHistory();
        }

        _historyTimer.Stop();
        _historyTimer.Start();
    }

    /// <summary>Ends the current burst of changes; the current values become the next step's starting point.</summary>
    internal void CloseHistoryStep()
    {
        _historyTimer.Stop();
        _historyBurst = false;
        _historyBaseline = _selectedProfile.Clone();
    }

    private void UndoAdjustment()
    {
        CloseHistoryStep();
        if (_undo.Count == 0) return;
        Push(_redo, _selectedProfile.Clone());
        Restore(Pop(_undo));
        StatusMessage = T("AdjustmentUndone");
    }

    private void RedoAdjustment()
    {
        CloseHistoryStep();
        if (_redo.Count == 0) return;
        Push(_undo, _selectedProfile.Clone());
        Restore(Pop(_redo));
        StatusMessage = T("AdjustmentRedone");
    }

    private void Restore(DisplayProfile values)
    {
        _applyingHistory = true;
        try
        {
            _selectedProfile.CopyAdjustmentsFrom(values);
        }
        finally
        {
            _applyingHistory = false;
        }

        _historyBaseline = _selectedProfile.Clone();
        RaiseHistory();
    }

    private static void Push(List<DisplayProfile> stack, DisplayProfile values)
    {
        stack.Add(values);
        if (stack.Count > MaxHistory) stack.RemoveAt(0);
    }

    private static DisplayProfile Pop(List<DisplayProfile> stack)
    {
        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }

    private void RaiseHistory()
    {
        Raise(nameof(CanUndoAdjustment));
        Raise(nameof(CanRedoAdjustment));
        CommandManager.InvalidateRequerySuggested();
    }
}

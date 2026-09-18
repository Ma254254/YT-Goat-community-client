using System.ComponentModel;
using GoatClient.ViewModels;

namespace GoatClient.Services.Dialogs;

/// <summary>In-window dialogs (overlay) – no extra windows, fully styled.</summary>
public interface IDialogService : INotifyPropertyChanged
{
    DialogViewModel? Current { get; }

    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool isDanger = false);

    Task ShowInfoAsync(string title, string message, string closeText = "Got it");

    /// <summary>Native folder picker. Returns null if cancelled.</summary>
    string? PickFolder(string? initialDirectory, string title);
}

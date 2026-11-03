using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

interface IHudShell
{
    FloatPanel MakePanel(string key, string title);
    Vector2 PanelSize(string key, float w, float h);
    IReadOnlyDictionary<string, FloatPanel> Panels { get; }
    void OpenPanel(string key);
    void ClosePanel(string key);
    void TogglePanel(string key);
    void SyncStripToggles();
    void PutPanelsBack();
    void Relayout();
    void Hook<TD>(Action<TD> on, Action<TD> off, TD handler) where TD : Delegate;
    void EnsureInGame(string defId);
    void FillTtsMenu(PopupMenu pm);
    void CopySavedLater(string title, string what, string error);
    Task CaptureSighting();
    void ShowSettings();
    void ShowSaveCopy();
    void SetLabelPlates(bool on);
}

public partial class Hud : IHudShell
{
    FloatPanel IHudShell.MakePanel(string key, string title) => MakePanel(key, title);
    Vector2 IHudShell.PanelSize(string key, float w, float h) => PanelSize(key, w, h);
    IReadOnlyDictionary<string, FloatPanel> IHudShell.Panels => Panels;
    void IHudShell.OpenPanel(string key) => OpenPanel(key);
    void IHudShell.ClosePanel(string key) => ClosePanel(key);
    void IHudShell.TogglePanel(string key) => TogglePanel(key);
    void IHudShell.SyncStripToggles() => SyncStripToggles();
    void IHudShell.PutPanelsBack() => PutPanelsBack();
    void IHudShell.Relayout() => Relayout();
    void IHudShell.Hook<TD>(Action<TD> on, Action<TD> off, TD handler) => Hook(on, off, handler);
    void IHudShell.EnsureInGame(string defId) => EnsureInGame(defId);
    void IHudShell.FillTtsMenu(PopupMenu pm) => FillTtsMenu(pm);
    void IHudShell.CopySavedLater(string title, string what, string error) => CopySavedLater(title, what, error);
    Task IHudShell.CaptureSighting() => CaptureSighting();
    void IHudShell.ShowSettings() => SettingsDialog.Open(Ctx, this, Top, Strip);
    void IHudShell.ShowSaveCopy() => SaveCopyDialog.Open(Ctx, this);
}

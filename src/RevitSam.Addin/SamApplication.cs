using Autodesk.Revit.UI;

namespace RevitSam.Addin;

public sealed class SamApplication : IExternalApplication
{
    private const string TabName = "Add-Ins";
    private const string PanelName = "SAM 3.1";

    public Result OnStartup(UIControlledApplication application)
    {
        var panel = application.GetRibbonPanels(TabName)
            .FirstOrDefault(existing => existing.Name == PanelName)
            ?? application.CreateRibbonPanel(TabName, PanelName);

        var button = new PushButtonData(
            "OpenSamWorkflow",
            "SAM 3.1\nWorkflow",
            typeof(SamApplication).Assembly.Location,
            typeof(OpenSamWorkflowCommand).FullName);
        button.ToolTip = "Check the local SAM 3.1 worker and detect assets in the active view.";
        panel.AddItem(button);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}

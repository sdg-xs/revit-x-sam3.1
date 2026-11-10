using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitSam.Addin;

[Transaction(TransactionMode.Manual)]
public sealed class OpenSamWorkflowCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var window = new SamWorkflowWindow(commandData.Application);
        window.ShowDialog();
        return Result.Succeeded;
    }
}


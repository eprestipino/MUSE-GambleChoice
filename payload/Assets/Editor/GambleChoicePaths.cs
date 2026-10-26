using System.IO;
using UnityEngine;

// Editor paths are project-relative; no workstation or parent-repository layout required.
public static class GambleChoicePaths
{
    public static string ProjectFolder => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    public static string TemplateFolder => Path.Combine(Application.dataPath, "Editor/GambleChoiceTemplates");
    public static string WorkingFolder
    {
        get
        {
            string localWorkspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            return Directory.Exists(Path.Combine(localWorkspace, "DevConfigs")) ? localWorkspace : ProjectFolder;
        }
    }
}

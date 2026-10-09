using UnityEditor;
using UnityEditor.SceneManagement;

namespace DriftSkate.EditorTools
{
    /// <summary>Startet den automatischen Spieltest im Editor (Kommandozeile: -executeMethod ... -autotest, ohne -quit).</summary>
    public static class AutoPlay
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.GaragePath);
            EditorApplication.isPlaying = true;
        }
    }
}

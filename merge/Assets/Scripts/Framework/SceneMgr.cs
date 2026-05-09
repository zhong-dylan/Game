using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneMgr : MonoSingle<SceneMgr>
{
    private AssetsLoader m_AssetsLoader;

    protected override void OnInit()
    {
        base.OnInit();
        m_AssetsLoader = gameObject.GetOrAddComponent<AssetsLoader>();
    }

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Log.Error("SceneMgr.LoadScene failed: sceneName is null or empty.");
            return;
        }

        m_AssetsLoader.LoadSceneAsync(sceneName, LoadSceneMode.Single);
    }

    public void LoadSceneAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Log.Error("SceneMgr.LoadSceneAsync failed: sceneName is null or empty.");
            return;
        }

        m_AssetsLoader.LoadSceneAsync(sceneName, LoadSceneMode.Single);
    }

    public void LoadSceneAdditive(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Log.Error("SceneMgr.LoadSceneAdditive failed: sceneName is null or empty.");
            return;
        }

        m_AssetsLoader.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    }

    public void LoadSceneAdditiveAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Log.Error("SceneMgr.LoadSceneAdditiveAsync failed: sceneName is null or empty.");
            return;
        }

        m_AssetsLoader.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
    }

    public void UnloadSceneAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Log.Error("SceneMgr.UnloadSceneAsync failed: sceneName is null or empty.");
            return;
        }

        m_AssetsLoader.UnloadSceneAsync(sceneName);
    }
}

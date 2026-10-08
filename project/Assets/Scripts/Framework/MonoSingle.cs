using UnityEngine;

/// <summary>
/// MonoBehaviour 单例基类，例如：public class GameManager : MonoSingle&lt;GameManager&gt;。
/// 实例随所在场景销毁；需要跨场景保留时，由派生类调用 DontDestroyOnLoad。
/// </summary>
public abstract class MonoSingle<T> : MonoBehaviour where T : MonoSingle<T>
{
    private static T instance;

    /// <summary>
    /// 优先使用场景中的实例（包括未激活对象），不存在时自动创建。
    /// 仅在 Unity 主线程访问。
    /// </summary>
    public static T Instance
    {
        get
        {
            if (instance == null)
                instance = FindObjectOfType<T>(true);

            if (instance == null)
            {
                var singletonObject = new GameObject(typeof(T).Name);
                instance = singletonObject.AddComponent<T>();
            }

            return instance;
        }
    }

    /// <summary>
    /// 查询是否已有实例，不会自动创建对象。
    /// </summary>
    public static bool HasInstance
    {
        get
        {
            if (instance == null)
                instance = FindObjectOfType<T>(true);

            return instance != null;
        }
    }

    // 派生类重写这些生命周期方法时，需要调用 base。
    protected virtual void Awake()
    {
        if (instance != null && instance != this)
        {
            // 只移除重复组件，保留同一对象上的其他组件。
            Destroy(this);
            return;
        }

        instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}

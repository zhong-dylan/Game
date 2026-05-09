using UnityEngine;

public abstract class MonoSingle<T> : MonoBehaviour where T : MonoSingle<T>
{
    private static T instance;
    private static readonly object locker = new object();
    private static bool isQuitting;

    public static T I
    {
        get
        {
            if (isQuitting)
            {
                return null;
            }

            lock (locker)
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<T>();
                }

                if (instance == null)
                {
                    GameObject singletonObject = new GameObject(typeof(T).Name);
                    instance = singletonObject.AddComponent<T>();
                }

                return instance;
            }
        }
    }

    protected virtual void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = (T)this;
        DontDestroyOnLoad(gameObject);
        OnInit();
    }

    protected virtual void OnApplicationQuit()
    {
        isQuitting = true;
    }

    protected virtual void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    protected virtual void OnInit()
    {
    }
}

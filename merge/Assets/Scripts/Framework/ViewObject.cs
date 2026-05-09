using System.Collections.Generic;
using UnityEngine;

public class ViewObject : MonoBehaviour
{
    [SerializeField] private UILayer m_Layer = UILayer.Main;
    [SerializeField] private bool m_UseRecycleQueue = true;
    [SerializeField] private bool m_NeverRelease;
    [SerializeField] private float m_ReleaseDelay = 30f;
    [SerializeField] private List<GameObject> m_GameObjects = new List<GameObject>();

    private readonly Dictionary<string, GameObject> m_ObjectMap = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, Queue<GameObject>> m_GameObjectPool = new Dictionary<string, Queue<GameObject>>();

    public UILayer Layer => m_Layer;
    public bool UseRecycleQueue => m_UseRecycleQueue;
    public bool NeverRelease => m_NeverRelease;
    public float ReleaseDelay => m_ReleaseDelay;

    private void Awake()
    {
        RebuildCache();
    }

    public GameObject Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (!m_GameObjectPool.TryGetValue(key, out Queue<GameObject> queue))
        {
            return null;
        }

        while (queue.Count > 0)
        {
            GameObject target = queue.Dequeue();
            if (target == null)
            {
                continue;
            }

            target.SetActive(true);
            return target;
        }

        return null;
    }

    public void Set(string key, GameObject go)
    {
        if (string.IsNullOrEmpty(key) || go == null)
        {
            return;
        }

        if (!m_GameObjectPool.TryGetValue(key, out Queue<GameObject> queue))
        {
            queue = new Queue<GameObject>();
            m_GameObjectPool.Add(key, queue);
        }

        go.SetActive(false);
        go.transform.SetParent(transform, false);
        queue.Enqueue(go);
    }

    public GameObject GetGameObject(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        m_ObjectMap.TryGetValue(name, out GameObject target);
        return target;
    }

    public T Get<T>(string name) where T : Component
    {
        GameObject target = GetGameObject(name);
        if (target == null)
        {
            return null;
        }

        return target.GetComponent<T>();
    }

    public void RebuildCache()
    {
        m_ObjectMap.Clear();
        for (int i = 0; i < m_GameObjects.Count; i++)
        {
            GameObject target = m_GameObjects[i];
            if (target == null || string.IsNullOrEmpty(target.name))
            {
                continue;
            }

            m_ObjectMap[target.name] = target;
        }
    }
}

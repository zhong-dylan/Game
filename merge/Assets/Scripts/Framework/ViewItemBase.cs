using System;
using UnityEngine;
using UnityEngine.UI;

public class OpenParam
{
    public GameObject Parent;
    public GameObject GameAsset;
    public GameObject GameObject;
    public string PrefabPath;
    public int IntValue;
    public object ObjValue;
}

public class ViewItemBase
{
    protected GameObject m_GameObject;
    protected OpenParam m_OpenParam;
    protected ViewObject m_ViewObject;

    public virtual string PrefabPath => GetType().Name;
    public GameObject GameObject => m_GameObject;
    public OpenParam OpenParam => m_OpenParam;

    public virtual void Init(OpenParam param)
    {
        m_OpenParam = param ?? new OpenParam();
        m_GameObject = null;
        SetViewObject(null);
        OnInit(m_OpenParam);
    }

    public virtual void BindGameObject(GameObject gameObject)
    {
        m_GameObject = gameObject;
        if (m_OpenParam != null)
        {
            m_OpenParam.GameObject = gameObject;
        }

        SetViewObject(m_GameObject == null ? null : m_GameObject.GetComponent<ViewObject>());
        OnInitUI();
    }

    public virtual void Show()
    {
        if (m_GameObject == null)
        {
            return;
        }

        m_GameObject.SetActive(true);
        OnEnable();
        OnShow();
    }

    public virtual void Hide()
    {
        if (m_GameObject == null)
        {
            return;
        }

        m_GameObject.SetActive(false);
        OnHide();
    }

    protected void SetViewObject(ViewObject viewObject)
    {
        m_ViewObject = viewObject;
    }

    protected void SetClickListener(string name, Action<GameObject> callback = null, Action<GameObject> onLongPress = null)
    {
        Button button = Get<Button>(name);
        if (button == null)
        {
            return;
        }

        UguiClickEventListener listener = button.gameObject.GetOrAddComponent<UguiClickEventListener>();
        listener.SetCallbacks(callback, onLongPress);
    }

    protected void SetText(string name, string text)
    {
        Text label = Get<Text>(name);
        if (label == null)
        {
            return;
        }

        label.text = text;
    }

    public T Get<T>(string name) where T : Component
    {
        if (m_ViewObject == null)
        {
            return null;
        }

        return m_ViewObject.Get<T>(name);
    }

    protected virtual void OnInit(OpenParam data)
    {
    }

    protected virtual void OnInitUI()
    {
    }

    protected virtual void OnEnable()
    {
    }

    protected virtual void OnShow()
    {
    }

    protected virtual void OnHide()
    {
    }
}

using UnityEngine;

public enum UILayer
{
    Main = 0,
    Pop = 1,
    Effect = 2,
    Guide = 3
}

public class PageBase : ViewItemBase
{
    protected GameObject m_RootGameObject;
    private bool m_IsClosed;

    public virtual string ViewName => GetType().Name;
    public virtual UILayer Layer => m_ViewObject == null ? UILayer.Main : m_ViewObject.Layer;
    public GameObject RootGameObject => m_RootGameObject;
    public ViewObject ViewObject => m_ViewObject;
    public bool IsVisible => m_RootGameObject != null && m_RootGameObject.activeSelf;

    public override void Init(OpenParam param)
    {
        m_OpenParam = param ?? new OpenParam();
        m_RootGameObject = m_OpenParam.Parent;
        base.Init(m_OpenParam);
    }

    public override void BindGameObject(GameObject gameObject)
    {
        base.BindGameObject(gameObject);
    }

    public override void Show()
    {
        if (m_RootGameObject == null)
        {
            return;
        }

        m_RootGameObject.SetActive(true);
        base.Show();
    }

    public override void Hide()
    {
        if (m_RootGameObject == null)
        {
            return;
        }

        m_RootGameObject.SetActive(false);
        base.Hide();
    }

    public virtual void Close()
    {
        if (m_IsClosed)
        {
            return;
        }

        UIMgr.I?.RemoveView(this);
    }

    internal void DestroyView()
    {
        if (m_IsClosed)
        {
            return;
        }

        m_IsClosed = true;
        OnClose();
        if (m_RootGameObject != null)
        {
            Object.Destroy(m_RootGameObject);
            m_RootGameObject = null;
        }

        m_GameObject = null;
        m_OpenParam = null;
        SetViewObject(null);
    }

    internal void RecycleView()
    {
        if (m_RootGameObject != null)
        {
            m_RootGameObject.SetActive(false);
        }
    }

    protected virtual void OnClose()
    {
    }
}

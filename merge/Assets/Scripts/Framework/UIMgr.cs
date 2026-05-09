using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIMgr : MonoSingle<UIMgr>
{
    private const int SortOrderStep = 100;

    private class LayerContext
    {
        public string Name;
        public Transform Root;
    }

    private class RecycledView
    {
        public PageBase View;
        public Coroutine ReleaseCoroutine;
    }

    private readonly Dictionary<string, PageBase> m_Views = new Dictionary<string, PageBase>();
    private readonly Dictionary<string, RecycledView> m_RecycledViews = new Dictionary<string, RecycledView>();
    private readonly Dictionary<UILayer, LayerContext> m_LayerContexts = new Dictionary<UILayer, LayerContext>();
    private readonly Dictionary<UILayer, List<PageBase>> m_LayerViews = new Dictionary<UILayer, List<PageBase>>();
    private readonly Stack<PageBase> m_PopStack = new Stack<PageBase>();

    private Transform m_UIRoot;

    protected override void OnInit()
    {
        base.OnInit();
        CreateUIRoot();
        RegisterLayer(UILayer.Main, "Main");
        RegisterLayer(UILayer.Pop, "Pop");
        RegisterLayer(UILayer.Effect, "Effect");
        RegisterLayer(UILayer.Guide, "Guide");
    }

    public void OpenView<T>(OpenParam param = null) where T : PageBase, new()
    {
        T view = GetView<T>();
        if (view != null)
        {
            BringViewToFront(view);
            if (view.Layer == UILayer.Pop)
            {
                HideCurrentPopView(view);
                PushPopView(view);
            }

            view.Show();
            return;
        }

        T newView = new T();
        OpenParam openParam = param ?? new OpenParam();
        if (TryRestoreRecycledView(newView.ViewName, out T recycledView, openParam))
        {
            if (recycledView.Layer == UILayer.Pop)
            {
                HideCurrentPopView();
                PushPopView(recycledView);
            }

            m_Views[recycledView.ViewName] = recycledView;
            BringViewToFront(recycledView);
            recycledView.Show();
            return;
        }

        GameObject viewRoot = CreateViewRoot(newView.ViewName, GetOrCreateLayerContext(UILayer.Main));
        openParam.Parent = viewRoot;
        newView.Init(openParam);

        CreateGameObject(newView.PrefabPath, openParam, instance =>
        {
            if (instance == null)
            {
                Object.Destroy(viewRoot);
                Log.Error($"UIMgr.OpenView failed: can not create view. viewName={newView.ViewName}");
                return;
            }

            instance.name = GetViewNodeName(newView.ViewName);
            newView.BindGameObject(instance);
            MoveViewToLayer(newView);
            BringViewToFront(newView);
            m_Views[newView.ViewName] = newView;

            if (newView.Layer == UILayer.Pop)
            {
                HideCurrentPopView();
                PushPopView(newView);
            }

            newView.Show();
        });
    }

    public void CreateViewItem<T>(OpenParam param, System.Action<T> onCreated = null) where T : ViewItemBase, new()
    {
        T item = new T();
        OpenParam openParam = param ?? new OpenParam();
        item.Init(openParam);

        CreateGameObject(item.PrefabPath, openParam, instance =>
        {
            if (instance == null)
            {
                Log.Error($"UIMgr.CreateViewItem failed: can not create item. itemType={typeof(T).Name}");
                onCreated?.Invoke(null);
                return;
            }

            item.BindGameObject(instance);
            item.Show();
            onCreated?.Invoke(item);
        });
    }

    public void CloseView<T>() where T : PageBase, new()
    {
        T view = GetView<T>();
        if (view == null)
        {
            return;
        }

        RemoveView(view);
    }

    public T GetView<T>() where T : PageBase, new()
    {
        string viewName = typeof(T).Name;
        if (!m_Views.TryGetValue(viewName, out PageBase view))
        {
            return null;
        }

        return view as T;
    }

    public void RemoveView(PageBase view)
    {
        if (view == null)
        {
            return;
        }

        m_Views.Remove(view.ViewName);
        bool wasTopPop = view.Layer == UILayer.Pop && m_PopStack.Count > 0 && m_PopStack.Peek() == view;
        RemovePopView(view);
        RemoveViewFromSorting(view);
        if (!RecycleView(view))
        {
            view.DestroyView();
        }

        if (wasTopPop)
        {
            ShowCurrentPopView();
        }
    }

    private void CreateGameObject(string defaultPrefabPath, OpenParam openParam, System.Action<GameObject> onCreated)
    {
        if (openParam.GameObject != null)
        {
            if (openParam.Parent != null)
            {
                openParam.GameObject.transform.SetParent(openParam.Parent.transform, false);
            }

            onCreated?.Invoke(openParam.GameObject);
            return;
        }

        if (openParam.GameAsset != null)
        {
            Transform parent = openParam.Parent == null ? null : openParam.Parent.transform;
            GameObject instance = Object.Instantiate(openParam.GameAsset, parent, false);
            onCreated?.Invoke(instance);
            return;
        }

        string prefabPath = string.IsNullOrEmpty(openParam.PrefabPath) ? defaultPrefabPath : openParam.PrefabPath;
        if (string.IsNullOrEmpty(prefabPath))
        {
            onCreated?.Invoke(null);
            return;
        }

        Transform parentTransform = openParam.Parent == null ? null : openParam.Parent.transform;
        AssetsLoader loader = GetOrAddAssetsLoader(openParam.Parent);
        if (loader == null)
        {
            Log.Error($"UIMgr.CreateGameObject failed: AssetsLoader missing. prefabPath={prefabPath}");
            onCreated?.Invoke(null);
            return;
        }

        loader.CreateGameObjectAsync(prefabPath, onCreated, parentTransform, false);
    }

    private void CreateUIRoot()
    {
        if (m_UIRoot != null)
        {
            return;
        }

        Transform existedRoot = transform.Find("UIRoot");
        if (existedRoot != null)
        {
            m_UIRoot = existedRoot;
            return;
        }

        m_UIRoot = new GameObject("UIRoot").transform;
        m_UIRoot.SetParent(transform, false);
    }

    private void RegisterLayer(UILayer layer, string layerName)
    {
        if (m_LayerContexts.ContainsKey(layer))
        {
            return;
        }

        GameObject layerRoot = new GameObject(layerName);
        layerRoot.transform.SetParent(m_UIRoot, false);

        RectTransform rootRect = layerRoot.AddComponent<RectTransform>();
        StretchFullScreen(rootRect);

        m_LayerContexts.Add(layer, new LayerContext
        {
            Name = layerName,
            Root = layerRoot.transform
        });

        if (!m_LayerViews.ContainsKey(layer))
        {
            m_LayerViews.Add(layer, new List<PageBase>());
        }
    }

    private LayerContext GetOrCreateLayerContext(UILayer layer)
    {
        if (m_LayerContexts.TryGetValue(layer, out LayerContext context))
        {
            return context;
        }

        RegisterLayer(layer, layer.ToString());
        m_LayerContexts.TryGetValue(layer, out context);
        return context;
    }

    private void HideCurrentPopView()
    {
        HideCurrentPopView(null);
    }

    private void HideCurrentPopView(PageBase ignoreView)
    {
        if (m_PopStack.Count == 0)
        {
            return;
        }

        PageBase current = m_PopStack.Peek();
        if (current == null || current == ignoreView)
        {
            return;
        }

        current.Hide();
    }

    private void ShowCurrentPopView()
    {
        if (m_PopStack.Count == 0)
        {
            return;
        }

        PageBase current = m_PopStack.Peek();
        current?.Show();
    }

    private void PushPopView(PageBase view)
    {
        if (view == null)
        {
            return;
        }

        RemovePopView(view);
        m_PopStack.Push(view);
    }

    private void RemovePopView(PageBase view)
    {
        if (view == null || m_PopStack.Count == 0)
        {
            return;
        }

        Stack<PageBase> tempStack = new Stack<PageBase>();
        bool removed = false;
        while (m_PopStack.Count > 0)
        {
            PageBase current = m_PopStack.Pop();
            if (current == view)
            {
                removed = true;
                break;
            }

            tempStack.Push(current);
        }

        while (tempStack.Count > 0)
        {
            m_PopStack.Push(tempStack.Pop());
        }

        if (!removed)
        {
            Log.Warning($"UIMgr.RemovePopView failed: {view.ViewName} not found in pop stack.");
        }
    }

    private static void StretchFullScreen(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }

    private static string GetViewNodeName(string viewName)
    {
        int splitIndex = viewName.LastIndexOf('/');
        if (splitIndex < 0 || splitIndex >= viewName.Length - 1)
        {
            return viewName;
        }

        return viewName.Substring(splitIndex + 1);
    }

    private GameObject CreateViewRoot(string viewName, LayerContext layerContext)
    {
        GameObject rootObject = new GameObject($"{GetViewNodeName(viewName)}Root");
        rootObject.transform.SetParent(layerContext.Root, false);

        RectTransform rectTransform = rootObject.AddComponent<RectTransform>();
        StretchFullScreen(rectTransform);

        Canvas canvas = rootObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = AppEntry.I == null ? null : AppEntry.I.MainCamera;
        canvas.overrideSorting = true;
        canvas.sortingLayerName = UILayer.Main.ToString();
        canvas.sortingOrder = 0;

        CanvasScaler canvasScaler = rootObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = Const.UIReferenceResolution;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 1f;

        rootObject.AddComponent<GraphicRaycaster>();
        rootObject.GetOrAddComponent<AssetsLoader>();
        return rootObject;
    }

    private void MoveViewToLayer(PageBase view)
    {
        if (view == null || view.RootGameObject == null)
        {
            return;
        }

        LayerContext layerContext = GetOrCreateLayerContext(view.Layer);
        if (layerContext == null)
        {
            Log.Error($"UIMgr.MoveViewToLayer failed: layer not found. layer={view.Layer}");
            return;
        }

        Transform rootTransform = view.RootGameObject.transform;
        if (rootTransform.parent != layerContext.Root)
        {
            rootTransform.SetParent(layerContext.Root, false);
        }

        Canvas canvas = view.RootGameObject.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingLayerName = view.Layer.ToString();
        }

        UpdateLayerSorting(view.Layer);
    }

    private AssetsLoader GetOrAddAssetsLoader(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return null;
        }

        return gameObject.GetOrAddComponent<AssetsLoader>();
    }

    private bool RecycleView(PageBase view)
    {
        ViewObject viewObject = view.ViewObject;
        if (viewObject == null || !viewObject.UseRecycleQueue)
        {
            return false;
        }

        CancelRecycleRelease(view.ViewName);
        view.RecycleView();

        RecycledView recycledView = new RecycledView
        {
            View = view
        };

        if (!viewObject.NeverRelease)
        {
            float delay = Mathf.Max(0f, viewObject.ReleaseDelay);
            recycledView.ReleaseCoroutine = StartCoroutine(ReleaseRecycledViewDelayed(view.ViewName, delay));
        }

        m_RecycledViews[view.ViewName] = recycledView;
        return true;
    }

    private bool TryRestoreRecycledView<T>(string viewName, out T view, OpenParam openParam) where T : PageBase
    {
        view = null;
        if (!m_RecycledViews.TryGetValue(viewName, out RecycledView recycledView) || recycledView.View == null)
        {
            return false;
        }

        CancelRecycleRelease(viewName);
        m_RecycledViews.Remove(viewName);

        view = recycledView.View as T;
        if (view == null)
        {
            recycledView.View.DestroyView();
            return false;
        }

        if (openParam != null && openParam.Parent != null && view.RootGameObject != openParam.Parent)
        {
            Object.Destroy(openParam.Parent);
        }

        MoveViewToLayer(view);
        return true;
    }

    private void BringViewToFront(PageBase view)
    {
        if (view == null)
        {
            return;
        }

        UILayer layer = view.Layer;
        List<PageBase> views = GetOrCreateLayerViews(layer);
        views.Remove(view);
        views.Add(view);
        UpdateLayerSorting(layer);
    }

    private void RemoveViewFromSorting(PageBase view)
    {
        if (view == null)
        {
            return;
        }

        UILayer layer = view.Layer;
        if (!m_LayerViews.TryGetValue(layer, out List<PageBase> views))
        {
            return;
        }

        if (views.Remove(view))
        {
            UpdateLayerSorting(layer);
        }
    }

    private List<PageBase> GetOrCreateLayerViews(UILayer layer)
    {
        if (!m_LayerViews.TryGetValue(layer, out List<PageBase> views))
        {
            views = new List<PageBase>();
            m_LayerViews[layer] = views;
        }

        return views;
    }

    private void UpdateLayerSorting(UILayer layer)
    {
        if (!m_LayerViews.TryGetValue(layer, out List<PageBase> views))
        {
            return;
        }

        for (int i = views.Count - 1; i >= 0; i--)
        {
            PageBase view = views[i];
            if (view == null || view.RootGameObject == null)
            {
                views.RemoveAt(i);
            }
        }

        for (int i = 0; i < views.Count; i++)
        {
            Canvas canvas = views[i].RootGameObject == null ? null : views[i].RootGameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                continue;
            }

            canvas.overrideSorting = true;
            canvas.sortingLayerName = layer.ToString();
            canvas.sortingOrder = i * SortOrderStep;
        }
    }

    private System.Collections.IEnumerator ReleaseRecycledViewDelayed(string viewName, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!m_RecycledViews.TryGetValue(viewName, out RecycledView recycledView))
        {
            yield break;
        }

        m_RecycledViews.Remove(viewName);
        recycledView.View?.DestroyView();
    }

    private void CancelRecycleRelease(string viewName)
    {
        if (!m_RecycledViews.TryGetValue(viewName, out RecycledView recycledView))
        {
            return;
        }

        if (recycledView.ReleaseCoroutine != null)
        {
            StopCoroutine(recycledView.ReleaseCoroutine);
            recycledView.ReleaseCoroutine = null;
        }
    }
}

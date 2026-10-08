using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 挂在开启 Raycast Target 的 Button、Image 等 UI 上，场景需有 EventSystem 和 GraphicRaycaster。
/// Get(target).onClick += go => ...，也可以在 Inspector 中绑定指针事件。
/// </summary>
[DisallowMultipleComponent]
public class UguiClickEventListener : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [Serializable]
    public class PointerEvent : UnityEvent<PointerEventData> { }

    public Action<GameObject> onEnter;
    public Action<GameObject> onExit;
    public Action<GameObject> onDown;
    public Action<GameObject> onUp;
    public Action<GameObject> onClick;

    [SerializeField] private PointerEvent pointerEnter = new PointerEvent();
    [SerializeField] private PointerEvent pointerExit = new PointerEvent();
    [SerializeField] private PointerEvent pointerDown = new PointerEvent();
    [SerializeField] private PointerEvent pointerUp = new PointerEvent();
    [SerializeField] private PointerEvent pointerClick = new PointerEvent();

    public static UguiClickEventListener Get(GameObject target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var listener = target.GetComponent<UguiClickEventListener>();
        return listener != null ? listener : target.AddComponent<UguiClickEventListener>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isActiveAndEnabled) return;
        onEnter?.Invoke(gameObject);
        pointerEnter.Invoke(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isActiveAndEnabled) return;
        onExit?.Invoke(gameObject);
        pointerExit.Invoke(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanInteract()) return;
        onDown?.Invoke(gameObject);
        pointerDown.Invoke(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!CanInteract()) return;
        onUp?.Invoke(gameObject);
        pointerUp.Invoke(eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!CanInteract()) return;
        onClick?.Invoke(gameObject);
        pointerClick.Invoke(eventData);
    }

    private bool CanInteract()
    {
        if (!isActiveAndEnabled) return false;
        var selectable = GetComponent<Selectable>();
        return selectable == null || selectable.IsInteractable();
    }
}

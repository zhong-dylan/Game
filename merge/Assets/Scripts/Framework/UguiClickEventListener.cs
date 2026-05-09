using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public class UguiClickEventListener : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    private const float PressScale = 0.95f;
    private const float TweenDuration = 0.08f;
    private const float LongPressThreshold = 0.5f;

    private bool m_IsPointerInside;
    private bool m_IsPointerDown;
    private bool m_HasLongPressed;
    private float m_PointerDownTime;
    private Vector3 m_OriginalScale = Vector3.one;
    private Transform m_TargetTransform;

    public Action<GameObject> OnClick;
    public Action<GameObject> OnLongPress;
    public Action OnPointerEnterEvent;
    public Action OnPointerExitEvent;
    public Action OnPointerDownEvent;
    public Action OnPointerUpEvent;

    private void Awake()
    {
        m_TargetTransform = transform;
        m_OriginalScale = m_TargetTransform.localScale;
    }

    private void OnEnable()
    {
        if (m_TargetTransform == null)
        {
            m_TargetTransform = transform;
        }

        m_OriginalScale = m_TargetTransform.localScale;
        ResetState(false);
    }

    private void OnDisable()
    {
        ResetState(true);
    }

    private void Update()
    {
        if (!m_IsPointerDown || m_HasLongPressed)
        {
            return;
        }

        if (Time.unscaledTime - m_PointerDownTime < LongPressThreshold)
        {
            return;
        }

        m_HasLongPressed = true;
        OnLongPress?.Invoke(gameObject);
    }

    public void SetCallbacks(Action<GameObject> onClick, Action<GameObject> onLongPress)
    {
        OnClick = onClick;
        OnLongPress = onLongPress;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        m_IsPointerInside = true;
        OnPointerEnterEvent?.Invoke();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_IsPointerInside = false;
        if (m_IsPointerDown)
        {
            PlayScale(m_OriginalScale);
        }

        OnPointerExitEvent?.Invoke();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        m_IsPointerDown = true;
        m_HasLongPressed = false;
        m_PointerDownTime = Time.unscaledTime;
        m_OriginalScale = m_TargetTransform.localScale;
        PlayScale(m_OriginalScale * PressScale);
        OnPointerDownEvent?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        m_IsPointerDown = false;
        PlayScale(m_OriginalScale);
        OnPointerUpEvent?.Invoke();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (m_HasLongPressed || !m_IsPointerInside)
        {
            return;
        }

        PlayScale(m_OriginalScale, true);
        OnClick?.Invoke(gameObject);
    }

    private void ResetState(bool resetScale)
    {
        m_IsPointerInside = false;
        m_IsPointerDown = false;
        m_HasLongPressed = false;
        if (resetScale && m_TargetTransform != null)
        {
            m_TargetTransform.DOKill();
            m_TargetTransform.localScale = m_OriginalScale;
        }
    }

    private void PlayScale(Vector3 scale, bool punch = false)
    {
        if (m_TargetTransform == null)
        {
            return;
        }

        m_TargetTransform.DOKill();
        if (punch)
        {
            m_TargetTransform.localScale = m_OriginalScale * PressScale;
        }

        m_TargetTransform.DOScale(scale, TweenDuration).SetUpdate(true);
    }
}

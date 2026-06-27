using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class UIPointerBridge : MonoBehaviour, 
    IPointerEnterHandler, 
    IPointerExitHandler,
    IPointerMoveHandler,
    IPointerDownHandler,
    IPointerUpHandler ,
    IBeginDragHandler,  // 新增
    IDragHandler,       // 新增
    IEndDragHandler     // 新增
{
    public Action<PointerEventData> onEnter;
    public Action<PointerEventData> onExit;
    public Action<PointerEventData> onMove;
    public Action<PointerEventData> onPointerDown;
    public Action<PointerEventData> onPointerUp;
    public Action<PointerEventData> onBeginDrag; // 新增
    public Action<PointerEventData> onDrag;      // 新增
    public Action<PointerEventData> onEndDrag;   // 新增
    public void OnPointerEnter(PointerEventData eventData)
    {
        //Debug.Log("进入 " + GetInstanceID());
        onEnter?.Invoke(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        onExit?.Invoke(eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        onMove?.Invoke(eventData);
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        onPointerDown?.Invoke(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        onPointerUp?.Invoke(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        onBeginDrag?.Invoke(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        onDrag?.Invoke(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        onEndDrag?.Invoke(eventData);
    }
}
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(RectTransform))]
public class MenuButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IPointerClickHandler
{
    [Header("위치 연출 설정")]
    [SerializeField] private float moveUpAmount = 10f; // 위로 올라갈 Y축 거리
    [SerializeField] private float moveSpeed = 15f;    // 이동/부드러움 속도

    [Header("색상 연출 설정")]
    [SerializeField] private Graphic targetGraphic;   // 색상을 변경할 대상 (미지정시 내 Image 자동 할당)
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selectedColor = new Color(0.75f, 0.75f, 0.75f, 1f); // 회색빛 돌게 변경

    [Header("씬 전환 설정 (선택 사항)")]
    [Tooltip("버튼 클릭 시 이동할 씬 이름을 적으세요. 비워두면 씬 전환을 하지 않습니다.")]
    [SerializeField] private string sceneToLoad;

    private RectTransform rectTransform;
    private Vector2 originalAnchoredPosition;
    private Vector2 targetAnchoredPosition;
    private Color targetColor;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        originalAnchoredPosition = rectTransform.anchoredPosition;
        targetAnchoredPosition = originalAnchoredPosition;

        if (targetGraphic == null)
        {
            targetGraphic = GetComponent<Graphic>();
        }

        if (targetGraphic != null)
        {
            targetGraphic.color = normalColor;
            targetColor = normalColor;
        }
    }

    private void Update()
    {
        // 위치 부드럽게 보간 이동 (Lerp)
        rectTransform.anchoredPosition = Vector2.Lerp(rectTransform.anchoredPosition, targetAnchoredPosition, Time.deltaTime * moveSpeed);

        // 색상 부드럽게 변경
        if (targetGraphic != null)
        {
            targetGraphic.color = Color.Lerp(targetGraphic.color, targetColor, Time.deltaTime * moveSpeed);
        }
    }

    // 선택 상태(호버 또는 키보드 선택) 활성화 연출
    private void OnSelected()
    {
        targetAnchoredPosition = originalAnchoredPosition + new Vector2(0f, moveUpAmount);
        targetColor = selectedColor;
    }

    // 선택 해제 상태 연출
    private void OnDeselected()
    {
        targetAnchoredPosition = originalAnchoredPosition;
        targetColor = normalColor;
    }

    #region EventSystem Interfaces Implementation

    // 마우스가 버튼 위에 올라갔을 때
    public void OnPointerEnter(PointerEventData eventData)
    {
        // 마우스 호버 시 EventSystem의 현재 선택 항목을 이 버튼으로 변경
        EventSystem.current.SetSelectedGameObject(gameObject);
    }

    // 마우스가 버튼 영역을 벗어났을 때
    public void OnPointerExit(PointerEventData eventData)
    {
        // 현재 선택된 버튼이 나 자신이라면 선택 해제하여 원래 상태로 복귀
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // 키보드/패드로 이 버튼이 선택되었을 때 (또는 SetSelectedGameObject 호출 시)
    public void OnSelect(BaseEventData eventData)
    {
        OnSelected();
    }

    // 키보드/패드로 선택이 다른 버튼으로 이동했거나 선택이 해제되었을 때
    public void OnDeselect(BaseEventData eventData)
    {
        OnDeselected();
    }

    // 버튼을 클릭했을 때 (마우스 클릭 / 패드 버턴 입력)
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!string.IsNullOrEmpty(sceneToLoad))
        {
            SceneManager.LoadScene(sceneToLoad);
        }
    }

    #endregion

    private void OnDisable()
    {
        // 비활성화 시 원래 상태로 초기화
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = originalAnchoredPosition;
        }
        if (targetGraphic != null)
        {
            targetGraphic.color = normalColor;
        }
    }
}
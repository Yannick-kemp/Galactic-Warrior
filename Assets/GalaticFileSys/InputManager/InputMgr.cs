using Assets.GalaticfFileSys;
using UnityEngine;

public class InputMgr : MonoBehaviour, IGame
{
    public static InputMgr Instance { get; private set; }

    public Vector2 TouchedVector { get; private set; }

    public bool InputLocked { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public Vector2 GetMovementInput()
    {
        if (InputLocked)
            return Vector2.zero;

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        return new Vector2(horizontal, vertical);
    }

    public bool IsJumpPressed()
    {
        if (InputLocked)
            return false;

        return Input.GetButtonDown("Jump");
    }

    //public bool IsScreenTouched()
    //{
    //    if (InputLocked)
    //        return false;

    //    var c = Input.mousePosition;

    //    if (Camera.main != null)
    //        TouchedVector = Camera.main.ScreenToWorldPoint(c);

    //    return Input.GetMouseButtonDown(0);
    //}
    public bool IsScreenTouched()
    {
        if (InputLocked)
            return false;

        var c = Input.mousePosition;

        if (Camera.main != null)
        {
            c.z = Mathf.Abs(Camera.main.transform.position.z);
            TouchedVector = Camera.main.ScreenToWorldPoint(c);
        }

        return Input.GetMouseButtonDown(0);
    }

    public void Initialize()
    {
        Debug.Log("InputManager Initialized");
    }
}
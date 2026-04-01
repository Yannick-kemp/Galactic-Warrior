using Assets.GalaticfFileSys;
using UnityEngine;

public class InputMgr : MonoBehaviour, IGame
{
    public static InputMgr Instance { get; private set; }
    public Vector2 TouchedVector
    {
        get;
        private set;
    }
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
        // Returns a 2D vector for movement (WASD/Arrow keys)
        float horizontal = Input.GetAxis("Horizontal"); // Set in Input settings by default
        float vertical = Input.GetAxis("Vertical");
        return new Vector2(horizontal, vertical);
    }

    public bool IsJumpPressed()
    {
        return Input.GetButtonDown("Jump"); // Space bar is the default
    }

    public bool IsScreenTouched()
    {
        var c = Input.mousePosition;
        TouchedVector = Camera.main.ScreenToWorldPoint(c);
        return Input.GetMouseButtonDown(0);
    }

    public void Initialize()
    {
        Debug.Log("InputManager Initialized");
    }


}

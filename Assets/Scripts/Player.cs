using Unity.Netcode;
using UnityEngine;


public class Player : NetworkBehaviour {


    public NetworkVariable<Vector3> PositionChange = new NetworkVariable<Vector3>();
    public NetworkVariable<Vector3> RotationChange = new NetworkVariable<Vector3>();
    public NetworkVariable<Color> PlayerColor = new NetworkVariable<Color>(Color.red);
    public NetworkVariable<int> Score = new NetworkVariable<int>(0);
    public NetworkVariable<int> Health = new NetworkVariable<int>(100);

    public TMPro.TMP_Text txtScoreDisplay;

    private GameManager _gameMgr;
    private Camera _camera;
    private Camera _fpsCamera;
    private BulletSpawner _bulletSpawner;

    [Header("Movement")]
    public float moveSpeed;

    public float groundDrag;

    public float jumpForce;
    public float jumpCooldown;
    public float airMultiplier;
    bool readyToJump;

    [HideInInspector] public float walkSpeed;
    [HideInInspector] public float sprintSpeed;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space;

    [Header("Ground Check")]
    public float playerHeight;
    public LayerMask whatIsGround;
    bool grounded;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;
    Vector3 spawnPoint;

    Rigidbody rb;

    // --------------------------
    // Behaviour
    // --------------------------

    private void Start() {
        ApplyPlayerColor();
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        spawnPoint = rb.transform.position;

        readyToJump = true;
        PlayerColor.OnValueChanged += OnPlayerColorChanged;
        _bulletSpawner = transform.Find("Camera").transform.Find("RArm").transform.Find("BulletSpawner").GetComponent<BulletSpawner>();
        _bulletSpawner.PlayerColor.Value = PlayerColor.Value;
    }

    public override void OnNetworkSpawn() {
        _camera = transform.Find("Camera").GetComponent<Camera>();
        _camera.enabled = IsOwner;

        _fpsCamera = transform.Find("Camera").transform.Find("FPSCamera").GetComponent<Camera>();
        _fpsCamera.enabled = IsOwner;

        Health.OnValueChanged += ClientOnScoreChanged;
        _bulletSpawner = transform.Find("Camera").transform.Find("RArm").transform.Find("BulletSpawner").GetComponent<BulletSpawner>();

        // Set layers for camera rendering
        if (IsOwner)
        {
            int FpsLayer = LayerMask.NameToLayer("FPSLayer");

            transform.Find("Camera").transform.Find("RArm").gameObject.layer = FpsLayer;
            foreach (Transform child in transform.Find("Camera").transform.Find("RArm").transform.Find("Cortis"))
            {
                child.gameObject.layer = FpsLayer;
            }
        }

        if (IsHost)
        {
            _bulletSpawner.BulletDamage.Value = 5;
            _bulletSpawner.PlayerColor.Value = Color.red;
        }
        DisplayScore();
    }

    private void Update()
    {
        if (IsOwner)
        {
            grounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.2f, whatIsGround);
            MyInput();
            SpeedControl();

            if (grounded)
            {
                rb.drag = groundDrag;
            }
            else
            {
                rb.drag = 0;
            }

            if (Input.GetButtonDown("Fire1"))
            {
                if (Health.Value > 0)
                {
                    _bulletSpawner.FireServerRpc();
                }
            }
        }

        if (!IsOwner || IsHost)
        {
            transform.Translate(PositionChange.Value);
            transform.Rotate(RotationChange.Value);
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            MovePlayer();
        }
    }

    // --------------------------
    // Private
    // --------------------------

    private void MyInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");

        // when to jump
        if (Input.GetKey(jumpKey) && readyToJump && grounded)
        {
            readyToJump = false;

            Jump();

            Invoke(nameof(ResetJump), jumpCooldown);
        }
    }

    private void MovePlayer()
    {
        // calculate movement direction
        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;


        // on ground
        if (grounded)
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);

        // in air
        else if (!grounded)
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
    }

    private void SpeedControl()
    {
        Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        // limit velocity if needed
        if (flatVel.magnitude > moveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.velocity = new Vector3(limitedVel.x, rb.velocity.y, limitedVel.z);
        }
    }

    private void Jump()
    {
        // reset y velocity
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }
    private void ResetJump()
    {
        readyToJump = true;
    }

    private void HostHandleBulletCollision(GameObject bullet)
    {
        if (Health.Value > 0)
        {
            // Modified bullets for Teams
            Bullet bulletScript = bullet.GetComponent<Bullet>();

            ulong ownerClientId = bullet.GetComponent<NetworkObject>().OwnerClientId;
            Player otherPlayer = NetworkManager.Singleton.ConnectedClients[ownerClientId].PlayerObject.GetComponent<Player>();

            if (PlayerColor.Value != otherPlayer.PlayerColor.Value)
            {
                Health.Value -= bulletScript.Damage.Value;
                if ( Health.Value <= 0)
                {
                    otherPlayer.Score.Value++;
                }
            }

        }

        Destroy(bullet);

    }

    private void HostHandleDamageBoostPickup(Collider other)
    {
        if(!_bulletSpawner.IsAtMaxDamage())
        {
            _bulletSpawner.IncreaseDamage();
            other.GetComponent<NetworkObject>().Despawn();
        }
    }


    // --------------------------
    // Events
    // --------------------------
    private void ClientOnScoreChanged(int previous, int current)
    {
        DisplayScore();
    }

    public void OnPlayerColorChanged(Color previous, Color current)
    {
        ApplyPlayerColor();
    }

    public void OnCollisionEnter(Collision collision)
    {
        if(IsHost)
        {
            if (collision.gameObject.CompareTag("Bullet"))
            {
                HostHandleBulletCollision(collision.gameObject);
            }
        }
    }

    public void OnTriggerEnter(Collider other)
    {
        if (IsHost)
        {
            if (other.gameObject.CompareTag("DamageBoost"))
            {
                HostHandleDamageBoostPickup(other);
            }
        }
    }

    // --------------------------
    // RPC
    // --------------------------

    [ServerRpc]
    public void RequestSetScoreServerRpc(int value)
    {
        Score.Value = value;
    }

    // --------------------------
    // Public
    // --------------------------
    public void ApplyPlayerColor() {
        GetComponent<MeshRenderer>().material.color = PlayerColor.Value;
        transform.Find("Camera").transform.Find("RArm").GetComponent<MeshRenderer>().material.color = PlayerColor.Value;
    }

    public void DisplayScore()
    {
        txtScoreDisplay.text = $"HP: {Health.Value} | SCORE: {Score.Value}";
        if (Health.Value <= 0)
        {
            txtScoreDisplay.text = "Dead";
            Respawn();
        }
    }

    public void Respawn()
    {
        // TODO: Create teleport system for characters on death
        Health.Value = 100;
    }

}
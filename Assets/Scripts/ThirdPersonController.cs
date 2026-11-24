using UnityEngine;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
using UnityEngine.InputSystem;
#endif

/* 注意：角色和胶囊体的动画都由控制器统一调用，内部对 Animator 做了空引用判断
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("角色")]
        [Tooltip("角色的移动速度（米/秒）")]
        public float MoveSpeed = 2.0f;

        [Tooltip("角色的冲刺速度（米/秒）")]
        public float SprintSpeed = 5.335f;

        [Tooltip("角色转向移动方向的速度有多快")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("加速与减速")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("玩家可以跳跃的高度")]
        public float JumpHeight = 1.2f;

        [Tooltip("角色使用自身的重力值，引擎默认为 -9.81")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("再次跳跃前需要等待的时间，设为 0 表示可以立即再次跳跃")]
        public float JumpTimeout = 0.50f;

        [Tooltip("进入下落状态前需要等待的时间，走下楼梯时很有用")]
        public float FallTimeout = 0.15f;

        [Header("角色接地检测")]
        [Tooltip("角色是否处于接地状态，这不是 CharacterController 内置的接地检测")]
        public bool Grounded = true;

        [Tooltip("用于起伏不平的地面")]
        public float GroundedOffset = -0.14f;

        [Tooltip("接地检测的半径，应和 CharacterController 的半径保持一致")]
        public float GroundedRadius = 0.28f;

        [Tooltip("角色把哪些层当作地面")]
        public LayerMask GroundLayers;

        [Header("虚拟相机（Cinemachine）")]
        [Tooltip("在 Cinemachine 虚拟相机上设置的跟随目标，相机会跟随它")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("镜头可以向上转动的最大角度（度）")]
        public float TopClamp = 70.0f;

        [Tooltip("镜头可以向下转动的最大角度（度）")]
        public float BottomClamp = -30.0f;

        [Tooltip("用于覆盖相机的额外角度，锁定相机时微调相机位置很有用")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("锁定相机在所有轴上的位置")]
        public bool LockCameraPosition = false;

        // 虚拟相机（Cinemachine）
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // 玩家
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // 超时计时器
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // 动画参数 ID
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
                return false;
#endif
            }
        }


        private void Awake()
        {
            // 获取主相机的引用
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
            _playerInput = GetComponent<PlayerInput>();
#else
            Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            AssignAnimationIDs();

            // 启动时重置各项计时器
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            if (DialogueSystem.DialogueManager.Instance != null &&
                DialogueSystem.DialogueManager.Instance.IsTalking)
            {
                _input.move = Vector2.zero;
                _input.jump = false;
                _input.sprint = false;
            }
            
            _hasAnimator = TryGetComponent(out _animator);

            JumpAndGravity();
            GroundedCheck();
            Move();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

        private void GroundedCheck()
        {
            // 计算检测球的位置，带一个偏移
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
                QueryTriggerInteraction.Ignore);

            // 如果使用角色模型，更新动画状态
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            // 如果有输入，并且相机位置没有被锁定
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                // 鼠标输入不需要乘以 Time.deltaTime
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            // 把旋转角度限制在 360 度以内
            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            // Cinemachine 会跟随这个目标
            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw, 0.0f);
        }

        private void Move()
        {
            // 根据移动速度、冲刺速度以及是否按下冲刺键来设置目标速度
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            // 一个简单的加减速逻辑，方便移除、替换或继续迭代

            // 注意：Vector2 的 == 运算符使用近似比较，不会因浮点误差出错，而且开销更小
            // 如果没有输入，就把目标速度设为 0
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            // 玩家当前水平速度的引用
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // 加速或减速到目标速度
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // 产生曲线变化而不是线性变化，让速度过渡更自然
                // 注意：Lerp 中的 T 会自动限制在 0~1 之间，所以不需要手动限制速度
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                // 把速度四舍五入到小数点后 3 位
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // 归一化输入方向
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // 注意：Vector2 的 != 运算符使用近似比较，不会因浮点误差出错，而且开销更小
            // 有移动输入时，让角色转向移动方向
            if (_input.move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                  _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);

                // 转向相对于相机方向的输入方向
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }


            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            // 移动玩家
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            // 如果使用角色模型，更新动画状态
            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // 重置下落超时计时器
                _fallTimeoutDelta = FallTimeout;

                // 如果使用角色模型，更新动画状态
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                // 接地时阻止垂直速度无限向下累积
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // 跳跃
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // 由 sqrt(H * -2 * G) 得到跳到目标高度所需的初速度
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    // 如果使用角色模型，更新动画状态
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                // 跳跃冷却
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // 重置跳跃冷却计时器
                _jumpTimeoutDelta = JumpTimeout;

                // 下落超时
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    // 如果使用角色模型，更新动画状态
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                // 如果不在接地状态，禁止跳跃
                _input.jump = false;
            }

            // 在低于终端速度时随时间累加重力（乘两次 deltaTime 让下落速度线性增长）
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // 选中时，在接地碰撞体的位置画出与半径一致的球体
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace BuildingTools
{
    /// <summary>
    /// 拖拽放置 + 表面吸附。挂在被拖动的"幽灵"物体上即可。
    ///
    /// 用法：
    ///   1. 新建空物体，把要放的门模型拖成它的子物体，挂上本脚本。
    ///   2. 组件 enabled 时物体跟随鼠标并贴到地面，左键确认，右键 / Esc 取消。
    ///
    /// 为什么 Y 轴能对齐（这是关键）：
    ///   · 落点取自射线的命中点，不是鼠标位移累加 —— 不会越拖越飘。
    ///   · Y 用"渲染包围盒的最低点"对齐地面，而不是 transform.position.y ——
    ///     所以轴心在门中间、在门顶、在门底，结果都一样贴地。
    ///   · 旋转 / 网格量化最后一次性写进 Transform，中间不产生半帧的错位。
    /// </summary>
    [DisallowMultipleComponent]
    public class SnapPlacer : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("留空则自动使用 Camera.main")]
        [SerializeField] private Camera targetCamera;

        [Header("可吸附的表面")]
        [Tooltip("射线只命中这些层。地板 / 墙请放到 Ground 层。")]
        [SerializeField] private LayerMask surfaceMask = 1 << 9; // Ground

        [Header("吸附")]
        [Tooltip("网格大小（米）。0 = 不吸网格。")]
        [SerializeField, Min(0f)] private float gridSize = 0.5f;

        [Tooltip("按包围盒左下角对齐网格，而不是按轴心。门这种要贴墙、贴边的物件建议勾上。")]
        [SerializeField] private bool alignGridByBounds;

        [Tooltip("让物体贴合表面法线（斜坡 / 斜面用）。平地放门请保持关闭，否则门会歪。")]
        [SerializeField] private bool alignToSurfaceNormal;

        [Tooltip("贴地之后再抬高一点，用来避免和地板 Z-fighting。")]
        [SerializeField] private float surfaceOffset;

        [Tooltip("射线最远距离（米）。")]
        [SerializeField, Min(0.1f)] private float maxRayDistance = 100f;

        [Header("旋转")]
        [Tooltip("按 R 每次旋转的角度；按住 Shift 反向。")]
        [SerializeField, Min(1f)] private float rotateStep = 90f;

        [Tooltip("滚轮每格微调的角度。")]
        [SerializeField, Min(1f)] private float fineRotateStep = 15f;

        [Header("合法性检测")]
        [Tooltip("是否检测和已有物体重叠。")]
        [SerializeField] private bool checkOverlap = true;

        [Tooltip("这些层不算阻挡（角色、地板等）。")]
        [SerializeField] private LayerMask ignoreLayers = (1 << 8) | (1 << 9); // Character | Ground

        [Tooltip("检测时每边收缩的距离（米）。贴墙放门时 0.02 左右正好，大了会穿墙。")]
        [SerializeField, Min(0f)] private float overlapMargin = 0.02f;

        [Header("合法性指示色")]
        [SerializeField] private Color validColor = new Color(0.25f, 1f, 0.4f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(1f, 0.25f, 0.2f, 0.45f);

        [Header("从 UI 拖出来时")]
        [Tooltip("勾上则松开左键才落地，避免刚生成就被同一次点击放下去。")]
        [SerializeField] private bool placeOnRelease;

        [Header("事件")]
        public UnityEvent onPlaced;
        public UnityEvent onCancelled;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

        private Renderer[] _renderers;
        private Collider[] _colliders;
        private MaterialPropertyBlock _block;
        private readonly Collider[] _overlaps = new Collider[16];

        private float _yaw;
        private bool _isValid;
        private int _tintState = -1; // -1 未设置，0 非法，1 合法

        /// <summary>当前落点是否合法（可供外部 UI 读取）。</summary>
        public bool IsValid => _isValid;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (targetCamera == null) targetCamera = Camera.main;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            // 拖拽期间关掉自己的碰撞体：射线不会打到自己，重叠检测也不会撞到自己
            SetCollidersEnabled(false);
            _yaw = transform.eulerAngles.y;
        }

        private void OnDisable()
        {
            SetCollidersEnabled(true);
        }

        private void Update()
        {
            if (targetCamera == null || Mouse.current == null) return;

            HandleRotateInput();

            if (TryGetSurface(out RaycastHit hit))
            {
                Apply(hit);
                _isValid = IsPlacementValid();
            }
            else
            {
                _isValid = false;  // 鼠标指到天空：脚下不动，只变红
            }

            ApplyTint();

            if (ShouldConfirm())
            {
                if (_isValid) Place();
                return;
            }

            if (Mouse.current.rightButton.wasPressedThisFrame ||
                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
            {
                Cancel();
            }
        }

        // ───────────────────────────── 核心：贴地 ─────────────────────────────

        /// <summary>把物体吸到鼠标指向的表面上。</summary>
        private void Apply(RaycastHit hit)
        {
            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (alignToSurfaceNormal)
                rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * rotation;

            // 先落到命中点（此时包围盒已按新朝向更新）
            transform.SetPositionAndRotation(hit.point, rotation);

            Bounds bounds = GetWorldBounds();
            Vector3 position = hit.point;
            // 关键一行：让包围盒最低点正好压在表面上，而不是让轴心压在表面上
            position.y += hit.point.y - bounds.min.y + surfaceOffset;

            if (gridSize > 0.0001f)
            {
                if (alignGridByBounds)
                {
                    position.x += SnapValue(bounds.min.x, gridSize) - bounds.min.x;
                    position.z += SnapValue(bounds.min.z, gridSize) - bounds.min.z;
                }
                else
                {
                    position.x = SnapValue(position.x, gridSize);
                    position.z = SnapValue(position.z, gridSize);
                }
            }

            transform.position = position;
        }

        private static float SnapValue(float value, float size) => Mathf.Round(value / size) * size;

        private bool TryGetSurface(out RaycastHit hit)
        {
            Vector2 screenPoint = Mouse.current.position.ReadValue();
            Ray ray = targetCamera.ScreenPointToRay(screenPoint);
            return Physics.Raycast(ray, out hit, maxRayDistance, surfaceMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>所有渲染器的世界包围盒（旋转后是轴对齐的 AABB，min.y 就是最低顶点）。</summary>
        private Bounds GetWorldBounds()
        {
            bool has = false;
            Bounds bounds = default;

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer renderer = _renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                    if (!has) { bounds = renderer.bounds; has = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!has && _colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    Collider collider = _colliders[i];
                    if (collider == null) continue;

                    if (!has) { bounds = collider.bounds; has = true; }
                    else bounds.Encapsulate(collider.bounds);
                }
            }

            if (!has) bounds = new Bounds(transform.position, Vector3.zero);
            return bounds;
        }

        // ───────────────────────────── 合法性 ─────────────────────────────

        private bool IsPlacementValid()
        {
            if (!checkOverlap) return true;

            Bounds bounds = GetWorldBounds();
            Vector3 halfExtents = bounds.extents - Vector3.one * overlapMargin;
            halfExtents = Vector3.Max(halfExtents, Vector3.one * 0.001f);

            int count = Physics.OverlapBoxNonAlloc(
                bounds.center, halfExtents, _overlaps, Quaternion.identity, ~(int)ignoreLayers);

            for (int i = 0; i < count; i++)
            {
                Collider other = _overlaps[i];
                if (other == null || other.isTrigger) continue;
                if (other.transform.IsChildOf(transform) || transform.IsChildOf(other.transform)) continue;
                return false;
            }

            return true;
        }

        // ───────────────────────────── 输入 ─────────────────────────────

        private void HandleRotateInput()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                float direction = Keyboard.current.shiftKey.isPressed ? -1f : 1f;
                _yaw = Mathf.Repeat(_yaw + rotateStep * direction, 360f);
            }

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                _yaw = Mathf.Repeat(_yaw + Mathf.Sign(scroll) * fineRotateStep, 360f);
        }

        private bool ShouldConfirm()
        {
            return placeOnRelease
                ? Mouse.current.leftButton.wasReleasedThisFrame
                : Mouse.current.leftButton.wasPressedThisFrame;
        }

        // ───────────────────────────── 对外接口 ─────────────────────────────

        /// <summary>确认放置。也可以在 UI 按钮里直接调用。</summary>
        public void Place()
        {
            ClearTint();
            enabled = false;   // 触发 OnDisable，重新打开碰撞体
            onPlaced?.Invoke();
        }

        /// <summary>取消并销毁幽灵物体。</summary>
        public void Cancel()
        {
            onCancelled?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>放置后想再次拖动时调用。</summary>
        public void BeginDrag()
        {
            enabled = true;
        }

        // ───────────────────────────── 外观 ─────────────────────────────

        private void ApplyTint()
        {
            int state = _isValid ? 1 : 0;
            if (state == _tintState || _renderers == null) return;
            _tintState = state;

            Color color = _isValid ? validColor : invalidColor;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void ClearTint()
        {
            _tintState = -1;
            if (_renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
            }
        }

        private void SetCollidersEnabled(bool value)
        {
            if (_colliders == null) return;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null) _colliders[i].enabled = value;
            }
        }
    }
}

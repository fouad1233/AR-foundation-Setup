using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Places a prefab (e.g. a cube) onto a detected AR surface when the user taps the screen
/// (on device) or clicks the left mouse button (in the Editor / XR Simulation).
///
/// How it works:
///   1. Every frame, check whether a new tap / click happened.
///   2. Cast a ray from that screen point into the AR world using ARRaycastManager.
///   3. If the ray hits a trackable (plane, feature point, ...), spawn the prefab
///      at the hit position with the hit's rotation, so it sits on the surface.
///   4. A short cooldown prevents multiple objects from being spawned by a single tap.
///
/// Setup: attach this script to the XR Origin GameObject, which also carries the
/// ARRaycastManager (and an ARPlaneManager so planes are detected), then assign
/// the prefab to spawn in the Inspector.
/// </summary>
public class ARPlaceCube : MonoBehaviour
{
    // Performs raycasts against AR trackables. Auto-filled in Awake() if left empty.
    [SerializeField] private ARRaycastManager raycastManager;

    // The object to instantiate at the hit point (assign in the Inspector).
    [SerializeField] private GameObject placementPrefab;

    // True while in the cooldown after a placement; blocks new placements.
    private bool isPlacing;

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }
    void Awake()
    {
        // If the field was not assigned in the Inspector, look for the manager
        // on the same GameObject (normally the XR Origin).
        if (!raycastManager) raycastManager = GetComponent<ARRaycastManager>();

        // Without a prefab nothing can be placed, so warn instead of failing silently.
        if (!placementPrefab) Debug.LogWarning("ARPlaceCube: Placement Prefab is not assigned.", this);
    }

    // Update is called once per frame
    void Update()
    {
        // No raycast manager means we cannot hit-test the AR world at all.
        if (!raycastManager) return;
        // if we are placing an object, we don't want to place another one
        if (isPlacing) return;

        // A "press" is either the first frame of a touch (device) or a left mouse click (Editor).
        bool pressed = false;
        Vector2 screenPosition = Vector2.zero;
        
        if (Touchscreen.current!=null)
        {
            // On a device, check for a new touch in the current frame.
            var primary = Touchscreen.current.primaryTouch;
            if (primary != null && primary.press.wasPressedThisFrame)
            {
                pressed = true;
                screenPosition = primary.position.ReadValue();
            }
            //PlaceObject(Input.GetTouch(0).position);
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // In the Editor, check for a left mouse click in the current frame.

            pressed = true;
            screenPosition = Mouse.current.position.ReadValue();
        
        }
        if (pressed )
        {
            
            isPlacing = true;
            PlaceObject(screenPosition);
        }


        // Re-enable placing after a short delay.
        StartCoroutine(SetIsPlacingToFalseWithDelay());
    }

    /// <summary>
    /// Raycasts from the given screen position and spawns the prefab at the closest hit.
    /// </summary>
    /// <param name="touchPosition">Screen-space position in pixels.</param>
    void PlaceObject(Vector2 touchPosition)
    {
        // ARRaycastManager fills this list with every trackable hit, sorted by distance.
        var rayHits = new List<ARRaycastHit>();

        // TrackableType.AllTypes: accept hits on planes, feature points, etc.
        // Use TrackableType.PlaneWithinPolygon to only allow placement inside detected planes.
        raycastManager.Raycast(touchPosition, rayHits, TrackableType.AllTypes);

        if (rayHits.Count > 0 && placementPrefab != null)
        {
            // rayHits[0] is the closest hit; its pose gives the world position and the
            // surface-aligned rotation (e.g. "up" matches the plane normal).
            Vector3 hitPosePosition = rayHits[0].pose.position;
            Quaternion hitPoseRotation = rayHits[0].pose.rotation;
            Instantiate(placementPrefab, hitPosePosition, hitPoseRotation);
        }
    }

    /// <summary>
    /// Cooldown so one tap / click spawns only one object.
    /// </summary>
    IEnumerator SetIsPlacingToFalseWithDelay()
    {
        yield return new WaitForSeconds(0.25f);
        isPlacing = false;
    }
}

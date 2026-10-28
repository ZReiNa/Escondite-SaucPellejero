using UnityEngine;

namespace HideAndSeek
{
    public class FPSLocalInput : MonoBehaviour
    {
        public float mouseSensitivity = 2.2f;
        public Transform cameraPivot;

        float m_yaw;
        float m_pitch;
        int m_tick;

        public InputCommand BuildInput()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                m_yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                m_pitch = Mathf.Clamp(m_pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -80f, 80f);
            }

            transform.rotation = Quaternion.Euler(0f, m_yaw, 0f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(m_pitch, 0f, 0f);

            return new InputCommand
            {
                tick = ++m_tick,
                moveX = Input.GetAxisRaw("Horizontal"),
                moveY = Input.GetAxisRaw("Vertical"),
                yaw = m_yaw,
                pitch = m_pitch,
                runHeld = Input.GetKey(KeyCode.LeftShift)
            };
        }
    }
}
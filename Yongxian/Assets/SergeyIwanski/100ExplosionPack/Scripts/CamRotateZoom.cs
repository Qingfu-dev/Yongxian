using UnityEngine;

namespace sergeyiwanski.explosions100
{
    public class CamRotateZoom : MonoBehaviour
    {
        public float mouseSence = 2.5f;
        public float maxDepth = 20;

        float depth = -15;
        float rotationX, rotationY;
        float minY = 1, maxY = 90, minDepthZ = 10;
        Quaternion newRotation;
        Vector3 startPosition;


        void Start()
        {
            rotationX = transform.rotation.eulerAngles.y;
            rotationY = transform.rotation.eulerAngles.x;
            newRotation = Quaternion.Euler(rotationY, rotationX, 0f);
            startPosition = transform.position;
        }

        // Update is called once per frame
        void Update()
        {
            Rotate();

            if (Input.GetMouseButton(2))
            {
                Zoom();
            }

            depth = Mathf.Clamp(depth + (Input.GetAxis("Mouse ScrollWheel") * 10), -maxDepth, -minDepthZ);
            transform.position = newRotation * new Vector3(startPosition.x, startPosition.y, depth);
        }


        void Rotate()
        {
            rotationX += (Input.GetAxis("Mouse X") * (mouseSence));
            rotationY -= (Input.GetAxis("Mouse Y") * (mouseSence));

            //Limit the angle of rotation of the camera along the Y axis
            rotationY = Mathf.Clamp(rotationY, minY, maxY);

            newRotation = Quaternion.Euler(rotationY, rotationX, 0f);
            transform.rotation = newRotation;
        }

        void Zoom()
        {
            //Zoom camera
            depth = Mathf.Clamp(depth + (Input.GetAxis("Mouse Y") * (mouseSence)), -maxDepth, -minDepthZ);
        }
    }
}

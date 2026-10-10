using UnityEngine;

namespace sergeyiwanski.explosions100
{
    public class ExplosionPackDemo : MonoBehaviour
    {
        [SerializeField]ParticleSystem[] explosions;
        int i = 0;
        string message;
        private GUIStyle guiStyle = new GUIStyle();

        // Use this for initialization
        void Start()
        {
            //explosions = GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem explosion in explosions)
            {
                explosion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            explosions[0].Play();
            message = "A - Back,   S - Current,   D - Next";

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        // Update is called once per frame
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                explosions[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ExplosionPlay(i-1);
            }
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                ExplosionPlay(i);
            }
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                explosions[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ExplosionPlay(i+1);
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                
            }
        }


        void ExplosionPlay(int j)
        {           
            i = (int)Mathf.Repeat(j, explosions.Length);
            explosions[i].Play();

           // explosions[i].transform.position = new Vector3(Random.Range(-5, 6), Random.Range(-2, 3), 0);
        }


        private void OnGUI()
        {
            guiStyle.fontSize = 16;
            guiStyle.normal.textColor = new Color(1, 1, 1, 0.9f);
            GUI.Label(new Rect(10, 10, 500, 30), message, guiStyle);
            GUI.Label(new Rect(10, 30, 500, 30), "Object:  " + explosions[i].name, guiStyle);
        }
    }
}

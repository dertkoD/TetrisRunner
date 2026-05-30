using UnityEngine;

public class Simple2DWater : MonoBehaviour
{
    public int pointCount = 40;
    public float width = 10f;
    public float springStrength = 0.02f;
    public float damping = 0.04f;
    public float spread = 0.04f;
    public LineRenderer line;
    public ParticleSystem splashParticles;

    float[] heights;
    float[] velocities;
    float baseY;

    void Start()
    {
        baseY = transform.position.y;
        heights = new float[pointCount];
        velocities = new float[pointCount];

        line.positionCount = pointCount;
        UpdateLine();
    }

    void FixedUpdate()
    {
        for (int i = 0; i < pointCount; i++)
        {
            float force = -springStrength * heights[i] - damping * velocities[i];
            velocities[i] += force;
            heights[i] += velocities[i];
        }

        float[] leftDeltas = new float[pointCount];
        float[] rightDeltas = new float[pointCount];

        for (int j = 0; j < 8; j++)
        {
            for (int i = 0; i < pointCount; i++)
            {
                if (i > 0)
                {
                    leftDeltas[i] = spread * (heights[i] - heights[i - 1]);
                    velocities[i - 1] += leftDeltas[i];
                }

                if (i < pointCount - 1)
                {
                    rightDeltas[i] = spread * (heights[i] - heights[i + 1]);
                    velocities[i + 1] += rightDeltas[i];
                }
            }
        }

        UpdateLine();
    }

    void UpdateLine()
    {
        for (int i = 0; i < pointCount; i++)
        {
            float x = -width / 2f + width * i / (pointCount - 1);
            line.SetPosition(i, new Vector3(x, baseY + heights[i], 0f));
        }
    }

    public void Splash(float worldX, float force)
    {
        float localX = worldX - transform.position.x;
        int index = Mathf.RoundToInt((localX + width / 2f) / width * (pointCount - 1));
        index = Mathf.Clamp(index, 0, pointCount - 1);

        velocities[index] += force;

        if (splashParticles != null)
        {
            var pos = new Vector3(worldX, baseY, 0f);
            splashParticles.transform.position = pos;
            splashParticles.Play();
        }
    }
}
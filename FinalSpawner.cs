using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using Unity.VisualScripting;
using UnityEngine;
//Burst Compiler
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using static UnityEngine.ParticleSystem;

public class FinalSpawner : MonoBehaviour
{
    [SerializeField] private Vector2Int pointGrid;
    private int points;
    [SerializeField] private GameObject circle;//17
    [SerializeField] private GameObject[] Circles;
    [SerializeField] private Vector2[] positions;
    [SerializeField] private Vector2[] velocities;
    [SerializeField] private float gravity;
    [SerializeField] private float damping;
    [SerializeField] private float radius;
    [SerializeField] private float affectRadius;
    [SerializeField] private float clampVelocity;
    [SerializeField] private Vector2 boxCol;
    [SerializeField] private Particle[] particles;
    [SerializeField] private ParticlePoint[] particlePoints;
    [SerializeField] private bool FromFile;
    [SerializeField] private bool ConstructFile;
    [SerializeField] private string name;
    private List<int>[,] cells;
    private int xMax;
    private int yMax;
    void Start()
    {
        points = pointGrid.x * pointGrid.y;
        Circles = new GameObject[points];
        positions = new Vector2[points];
        velocities = new Vector2[points];

        particlePoints = new ParticlePoint[points];

        xMax = (Mathf.FloorToInt(boxCol.x / affectRadius) + 1);
        yMax = (Mathf.FloorToInt(boxCol.y / affectRadius) + 1);
        SetCells();

        RNDSetPosition();

        if (FromFile)
        {
            using (FileStream stream = File.Open(GetFilePath(), FileMode.Open))
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    Debug.Log(GetFilePath());
                    particles = new Particle[reader.ReadInt32()];
                    for (int i = 0; i < particles.Length; i++)
                    {
                        particles[i] = new Particle();
                        float r = reader.ReadSingle();
                        float g = reader.ReadSingle();
                        float b = reader.ReadSingle();
                        particles[i].color = new Color(r, g, b);
                    }
                    ParticlePoint.attractions = new float[particles.Length * particles.Length];
                    for (int x = 0; x < particles.Length; x++)
                    {
                        for (int y = 0; y < particles.Length; y++)
                        {
                            ParticlePoint.attractions[AttractionIndex(x, y)] = reader.ReadSingle();
                        }
                    }
                }
            }
        }
        else SetAttractions();

        for (int i = 0; i < points; i++)
        {
            particlePoints[i] = new ParticlePoint();
            int particleIndex = i % particles.Length;

            particlePoints[i].pointObject = DrawCircle(positions[i], particles[particleIndex].color);
            particlePoints[i].pointObject.name = i.ToString();
        }

        SetCells();
    }

    void Update()
    {
        CellPhysicsLoop();
    }

    public void SetCells()
    {
        cells = new List<int>[xMax, yMax];
        for (int x = 0; x < xMax; x++)
        {
            for (int y = 0; y < yMax; y++)
            {
                cells[x, y] = new List<int>();
            }
        }
    }
    public void ClearCells()
    {
        for (int x = 0; x < xMax; x++)
        {
            for (int y = 0; y < yMax; y++)
            {
                cells[x, y].Clear();
            }
        }
    }

    public void PhysicsLoop()
    {
        for (int i = 0; i < points; i++)
        {
            int p1 = i % particles.Length; //particleIndex1
            int p2 = 0; //particleIndex2

            Vector2 posI = positions[i];
            Vector2 velocityI = velocities[i];
            for (int k = i + 1; k < points; k++)
            {
                Vector2 posK = positions[k];
                p2 = k % particles.Length;

                float magnitudeSqr = (posI - posK).sqrMagnitude;
                if (magnitudeSqr + .1f <= affectRadius * affectRadius)
                {
                    float magnitude = Mathf.Sqrt(magnitudeSqr);
                    Vector2 direction = (posI - posK).normalized;
                    float dist = affectRadius - magnitude;
                    Vector2 force1 = direction * AttractAtDist(dist) * ParticlePoint.attractions[AttractionIndex(p1, p2)];
                    Vector2 force2 = -direction * AttractAtDist(dist) * ParticlePoint.attractions[AttractionIndex(p2, p1)];
                    velocityI += Vector2.ClampMagnitude(force1, 3f) * Time.deltaTime;
                    velocities[k] += Vector2.ClampMagnitude(force2, 3f) * Time.deltaTime;
                }
            }


            velocityI.y -= gravity * Time.deltaTime;
            velocityI = Vector2.ClampMagnitude(velocityI, clampVelocity);
            velocityI += UnityEngine.Random.insideUnitCircle * Time.deltaTime;
            velocityI *= .99f;
            positions[i] += velocityI;
            velocities[i] = velocityI;
            particlePoints[i].pointObject.transform.position = positions[i];

            Bounds(i);
        }

        ConstructFileFunc();
    }
    public void CellPhysicsLoop()
    {
        ClearCells();
        for (int i = 0; i < points; i++)
        {
            Vector2 posI = positions[i];
            int p1 = i % particles.Length;
            int cellPosx = Mathf.FloorToInt((posI.x + boxCol.x / 2f) / affectRadius);
            int cellPosy = Mathf.FloorToInt((posI.y + boxCol.y / 2f) / affectRadius);

            cellPosx = Mathf.Clamp(cellPosx, 0, cells.GetLength(0) - 1);
            cellPosy = Mathf.Clamp(cellPosy, 0, cells.GetLength(1) - 1);
            cells[cellPosx, cellPosy].Add(i);
            Vector2 velocityI = velocities[i];
            for (int x = cellPosx - 1; x <= cellPosx + 1; x++)
            {
                for (int y = cellPosy - 1; y <= cellPosy + 1; y++)
                {
                    if (x < 0 || y < 0 || x >= cells.GetLength(0) || y >= cells.GetLength(1)) continue;
                    for (int j = 0; j < cells[x, y].Count; j++)
                    {
                        int k = cells[x, y][j];
                        if (k == i) continue;

                        Vector2 posK = positions[k];
                        int p2 = k % particles.Length;

                        float magnitudeSqr = (posI - posK).sqrMagnitude;
                        if (magnitudeSqr + .1f <= affectRadius * affectRadius)
                        {
                            float magnitude = Mathf.Sqrt(magnitudeSqr);
                            Vector2 direction = (posI - posK).normalized;
                            float dist = affectRadius - magnitude;
                            Vector2 force1 = direction * AttractAtDist(dist) * ParticlePoint.attractions[AttractionIndex(p1, p2)];
                            Vector2 force2 = -direction * AttractAtDist(dist) * ParticlePoint.attractions[AttractionIndex(p2, p1)];
                            velocityI += Vector2.ClampMagnitude(force1, 3f) * Time.deltaTime;
                            velocities[k] += Vector2.ClampMagnitude(force2, 3f) * Time.deltaTime;
                        }
                    }
                }
            }
            velocityI.y -= gravity * Time.deltaTime;
            velocityI = Vector2.ClampMagnitude(velocityI, clampVelocity);
            velocityI += UnityEngine.Random.insideUnitCircle * Time.deltaTime;
            velocityI *= .99f;
            positions[i] += velocityI;
            velocities[i] = velocityI;
            particlePoints[i].pointObject.transform.position = positions[i];

            Bounds(i);
        }
        ConstructFileFunc();
    }

    public string GetFilePath()
    {
        string fileName = Path.Combine("Assets/AttractionFiles/", name + ".dat");
        return fileName;
    }

    public void ConstructFileFunc()
    {
        if (ConstructFile)
        {
            string fileName = GetFilePath();
            using (FileStream stream = File.Open(fileName, FileMode.Create))
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(particles.Length);
                    for (int i = 0; i < particles.Length; i++)
                    {
                        writer.Write(particles[i].color.r);
                        writer.Write(particles[i].color.g);
                        writer.Write(particles[i].color.b);
                    }

                    for (int x = 0; x < particles.Length; x++)
                    {
                        for (int y = 0; y < particles.Length; y++)
                        {
                            writer.Write(ParticlePoint.attractions[AttractionIndex(x, y)]);
                        }
                    }
                }
            }
            ConstructFile = false;
            Debug.Log("Constructed");
        }
    }


    public int AttractionIndex(int i, int j)
    {
        return (i * particles.Length) + j;
    }

    public void Bounds(int i)
    {

        if (positions[i].y < -boxCol.y / 2f + radius / 2f)
        {
            positions[i].y = -boxCol.y / 2f + radius / 2f;
            velocities[i] *= -1 * damping;
        }
        if (positions[i].y > boxCol.y / 2f - radius / 2f)
        {
            positions[i].y = boxCol.y / 2f - radius / 2f;
            velocities[i] *= -1 * damping;
        }

        if (positions[i].x < -boxCol.x / 2f + radius / 2f)
        {
            positions[i].x = -boxCol.x / 2f + radius / 2f;
            velocities[i] *= -1 * damping;
        }
        if (positions[i].x > boxCol.x / 2f - radius / 2f)
        {
            positions[i].x = boxCol.x / 2f - radius / 2f;
            velocities[i] *= -1 * damping;
        }
    }

    public void InfBounds(int i)
    {
        if (positions[i].y < -boxCol.y / 2f + radius / 2f)
        {
            positions[i].y = boxCol.y / 2f - radius / 2f;
        }
        if (positions[i].y > boxCol.y / 2f - radius / 2f)
        {
            positions[i].y = -boxCol.y / 2f + radius / 2f;
        }

        if (positions[i].x < -boxCol.x / 2f + radius / 2f)
        {
            positions[i].x = boxCol.x / 2f - radius / 2f;
        }
        if (positions[i].x > boxCol.x / 2f - radius / 2f)
        {
            positions[i].x = -boxCol.x / 2f + radius / 2f;
        }
    }
    public void SetPosition()
    {
        Vector2 offSet = new Vector2(pointGrid.x / 2f - .5f, pointGrid.y / 2f - .5f);
        for (int y = 0; y < pointGrid.y; y++)
        {
            for (int x = 0; x < pointGrid.x; x++)
            {
                positions[(y * pointGrid.x) + x] = new Vector2((x - offSet.x) * 3f, (y - offSet.y) * 3f);
            }
        }
    }

    public void RNDSetPosition()
    {
        for (int y = 0; y < pointGrid.y; y++)
        {
            for (int x = 0; x < pointGrid.x; x++)
            {
                positions[(y * pointGrid.x) + x] = new Vector2(UnityEngine.Random.Range(-boxCol.x / 2f, boxCol.x / 2f), UnityEngine.Random.Range(-boxCol.y / 2f, boxCol.y / 2f));
            }
        }
    }


    public void SetAttractions()
    {
        ParticlePoint.attractions = new float[particles.Length * particles.Length];
        for (int i = 0; i < particles.Length; i++)
        {
            for (int j = 0; j < particles.Length; j++)
            {
                ParticlePoint.attractions[AttractionIndex(i, j)] = 3 * UnityEngine.Random.Range(-1f, 1f);

                Debug.Log(ParticlePoint.attractions[AttractionIndex(i, j)]);
            }
        }
    }

    public GameObject DrawCircle(Vector2 circlePos, Color color)
    {
        GameObject Circle = Instantiate(circle, circlePos, Quaternion.identity);
        Circle.transform.localScale = new Vector2(radius, radius);
        Circle.GetComponent<SpriteRenderer>().color = color;
        return Circle;
    }

    public void OnDrawGizmos()
    {
        Gizmos.DrawCube(Vector3.zero, new Vector3(boxCol.x, boxCol.y, 1));
    }

    public float AttractAtDist(float x)
    {
        float r0 = 1.5f;
        float B = 1f;
        float a6 = r0 * r0 * r0 * r0 * r0 * r0;
        float A = B * a6;

        float x6 = x * x * x * x * x * x;
        float x12 = x6 * x6;
        float Lennard = ((A / x12) - (B / x6));

        float y = x % (2f * Mathf.PI);
        if (y > Mathf.PI) y -= 2f * Mathf.PI;
        else if (y < Mathf.PI) y += Mathf.PI;

        return Lennard + (Sin(y) * .25f);
    }

    public float Sin(float x)
    {
        float x2 = x * x;
        float x3 = x * x2;
        float x5 = x3 * x * x;
        float x7 = x3 * x3 * x;
        float x9 = x7 * x2;

        return x - (x3 / 6f) + (x5 / 120f) - (x7 / 5040f) + (x9 / 362880f);
    }
}

[System.Serializable]
public class Particle
{
    public Color color;
}
public class ParticlePoint
{
    public GameObject pointObject;
    public Particle particle;
    public static float[] attractions;
}

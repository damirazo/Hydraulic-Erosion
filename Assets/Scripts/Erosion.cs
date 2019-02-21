﻿// precomp brushes
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Erosion : MonoBehaviour {

    public int seed;
    public int erosionRadius = 3;
    [Range (0, 1)]
    public float inertia = .05f; // At zero, water will instantly change direction to flow downhill. At 1, water will never change direction. 
    public float sedimentCapacityFactor = 4; // Multiplier for how much sediment a droplet can carry
    public float minSedimentCapacity = .01f; // Used to prevent carry capacity getting too close to zero on flatter terrain
    [Range (0, 1)]
    public float erodeSpeed = .1f;
    [Range (0, 1)]
    public float depositSpeed = .1f;
    [Range (0, 1)]
    public float evaporateSpeed = .1f;
    public float gravity = 1;
    public int maxDropletLifetime = 30;

    System.Random prng;
    bool initialized;

    // Indices and weights of erosion brush precomputed for every node
    int[][] erosionBrushIndices;
    float[][] erosionBrushWeights;

    // Debug vars
    [Header ("Debug:")]
    public List<Vector3> debugPositions;
    public float d_amountEroded;
    public float d_amountDeposited;
    public float d_deltaSediment;

    public void Erode (float[] nodes, int mapSize) {
        //debugPositions = new List<Vector3> ();

        if (!initialized || prng == null) {
            initialized = true;
            prng = new System.Random (seed);
            InitializeBrushIndices (mapSize, erosionRadius);
        }

        // Create water droplet at random point on map
        Vector2 randomPos = new Vector2 (prng.Next (0, mapSize - 1), prng.Next (0, mapSize - 1)) + Vector2.one * .5f; // place in middle of random cell
        WaterDroplet droplet = new WaterDroplet () { position = randomPos, waterVolume = 1, speed = 1 };

        for (int lifetime = 0; lifetime < maxDropletLifetime; lifetime++) {
            Vector2Int dropletCoord = new Vector2Int ((int) droplet.position.x, (int) droplet.position.y);
            int dropletIndex = dropletCoord.y * mapSize + dropletCoord.x;
            // Calculate direction of flow from the height difference of surrounding points
            var point = CalculateHeightAndGradient (nodes, mapSize, droplet.position);

            // Update the droplet's direction, speed, position, and apply evaporation
            droplet.direction = (droplet.direction * inertia - point.gradient * (1 - inertia)).normalized;
            // Give droplet random direction if is on flat surface
            if (droplet.direction == Vector2.zero) {
                float randomAngle = (float) prng.NextDouble () * Mathf.PI * 2;
                droplet.direction = new Vector2 (Mathf.Sin (randomAngle), Mathf.Cos (randomAngle));
            }

            Vector2 positionOld = droplet.position;
            droplet.position += droplet.direction;
            //debugPositions.Add (new Vector3 (positionOld.x, point.height, positionOld.y));

            // Stop simulating droplet if it has flowed over edge of map
            if (droplet.position.x < 0 || droplet.position.y < 0 || droplet.position.x >= mapSize - 1 || droplet.position.y >= mapSize - 1) {
                break;
            }

            // Calculate new and old height of droplet
            float newHeight = CalculateHeightAndGradient (nodes, mapSize, droplet.position).height;
            float deltaHeight = newHeight - point.height; // negative if moving downwards

            // Calculate the sediment carry capacity of the droplet. Can carry more when moving fast downhill.
            float sedimentCapacity = Mathf.Max (-deltaHeight * droplet.speed * droplet.waterVolume * sedimentCapacityFactor, minSedimentCapacity);

            if (droplet.sediment > sedimentCapacity || deltaHeight > 0) {
                // Deposit a fraction of the surplus sediment
                float amountToDeposit = (droplet.sediment - sedimentCapacity) * depositSpeed;
                // If moving uphill, try fill the pit the droplet has just left
                if (deltaHeight > 0) {
                    amountToDeposit = Mathf.Min (deltaHeight, droplet.sediment);
                }

                // Add the sediment to the four nodes of the current cell using bilinear interpolation
                // Deposition is not distributed over a radius (like erosion) so that it can fill small pits
                Vector2 offset = positionOld - dropletCoord;
                int nodeIndexNW = dropletCoord.y * mapSize + dropletCoord.x;
                nodes[nodeIndexNW] += amountToDeposit * (1 - offset.x) * (1 - offset.y);
                nodes[nodeIndexNW + 1] += amountToDeposit * (offset.x) * (1 - offset.y);
                nodes[nodeIndexNW + mapSize] += amountToDeposit * (1 - offset.x) * (offset.y);
                nodes[nodeIndexNW + mapSize + 1] += amountToDeposit * (offset.x) * (offset.y);

                droplet.sediment -= amountToDeposit;
                d_amountDeposited += amountToDeposit;

            } else {
                // Erode from the terrain a fraction of the droplet's current carry capacity.
                // Clamp the erosion to the change in height so that it never digs a hole in the terrain (can at most flatten).
                float amountToErode = Mathf.Min ((sedimentCapacity - droplet.sediment) * erodeSpeed, -deltaHeight);

                // Use erosion brush to erode from all nodes inside radius
                for (int brushPointIndex = 0; brushPointIndex < erosionBrushIndices[dropletIndex].Length; brushPointIndex++) {
                    int nodeIndex = erosionBrushIndices[dropletIndex][brushPointIndex];
                    // Don't erode below zero (to avoid very deep erosion from occuring)
                    float sediment = Mathf.Min (nodes[nodeIndex], amountToErode * erosionBrushWeights[dropletIndex][brushPointIndex]);
                    nodes[nodeIndex] -= sediment;
                    droplet.sediment += sediment;
                    d_amountEroded += sediment;
                }
            }

            droplet.speed = Mathf.Sqrt (droplet.speed * droplet.speed + deltaHeight * gravity);
            droplet.waterVolume *= (1 - evaporateSpeed);

        }

        d_deltaSediment = d_amountDeposited - d_amountEroded;
    }

    HeightAndGradient CalculateHeightAndGradient (float[] nodes, int mapSize, Vector2 pos) {

        Vector2Int coord = new Vector2Int ((int) pos.x, (int) pos.y);
        // Calculate droplet's offset inside the cell (0,0) = at NW node, (1,1) = at SE node
        Vector2 offset = pos - coord;

        // Calculate heights of the four nodes of the droplet's cell
        int nodeIndexNW = coord.y * mapSize + coord.x;
        float heightNW = nodes[nodeIndexNW];
        float heightNE = nodes[nodeIndexNW + 1];
        float heightSW = nodes[nodeIndexNW + mapSize];
        float heightSE = nodes[nodeIndexNW + mapSize + 1];

        // Calculate droplet's direction of flow with bilinear interpolation of height difference along the edges
        float flowDirectionX = (heightNE - heightNW) * (1 - offset.y) + (heightSE - heightSW) * offset.y;
        float flowDirectionY = (heightSW - heightNW) * (1 - offset.x) + (heightSE - heightNE) * offset.x;
        Vector2 flowDirection = new Vector2 (flowDirectionX, flowDirectionY);

        // Calculate height with bilinear interpolation of the heights of the nodes of the cell
        float height = heightNW * (1 - offset.x) * (1 - offset.y) + heightNE * offset.x * (1 - offset.y) + heightSW * (1 - offset.x) * offset.y + heightSE * offset.x * offset.y;

        return new HeightAndGradient () { height = height, gradient = flowDirection };
    }

    struct HeightAndGradient {
        public float height;
        public Vector2 gradient;
    }

    struct WaterDroplet {
        public Vector2 velocity;
        public Vector2 position;
        public Vector2 direction;
        public float speed;
        public float sediment;
        public float waterVolume;
    }

    void InitializeBrushIndices (int mapSize, int radius) {
        erosionBrushIndices = new int[mapSize * mapSize][];
        erosionBrushWeights = new float[mapSize * mapSize][];

        int[] indices = new int[radius * radius * 4];
        float[] weights = new float[radius * radius * 4];

        for (int i = 0; i < erosionBrushIndices.GetLength (0); i++) {
            Vector2Int centre = new Vector2Int (i % mapSize, i / mapSize);
            float weightSum = 0;
            int addIndex = 0;

            for (int y = -radius; y <= radius; y++) {
                for (int x = -radius; x <= radius; x++) {
                    float sqrDst = x * x + y * y;
                    if (sqrDst < radius * radius) {
                        Vector2Int coord = new Vector2Int (x, y) + centre;

                        if (coord.x >= 0 && coord.x < mapSize && coord.y >= 0 && coord.y < mapSize) {
                            float weight = 1 - Mathf.Sqrt (sqrDst) / radius;
                            weightSum += weight;
                            weights[addIndex] = weight;
                            indices[addIndex] = coord.y * mapSize + coord.x;
                            addIndex++;
                        }
                    }
                }
            }

            int numEntries = addIndex;
            erosionBrushIndices[i] = new int[numEntries];
            erosionBrushWeights[i] = new float[numEntries];
            
            for (int j = 0; j < numEntries; j++) {
                erosionBrushIndices[i][j] = indices[j];
                erosionBrushWeights[i][j] = weights[j] / weightSum;
            }
        }

    }
}
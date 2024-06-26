
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Trajectory = System.Collections.Generic.List<EBD.TrajectoryEntry>;

namespace EBD
{
    public static class DensityHeatmap
    {
        private static (Vector3, Vector3) GetBounds(Dictionary<string, Trajectory> trajectories)
        {
            // Combine all trajectories into one large trajectory.
            Trajectory allTrajectories = new Trajectory();
            foreach (KeyValuePair<string, Trajectory> entry in trajectories)
            {
                allTrajectories.AddRange(entry.Value);
            }

            // Get positions.
            List<Vector3> positions = allTrajectories.Select(entry => entry.Position).ToList();

            // Get min and max bounds.
            Vector3 minBounds = new Vector3(
                positions.Min(p => p.x),
                positions.Min(p => p.y),
                positions.Min(p => p.z)
            );

            Vector3 maxBounds = new Vector3(
                positions.Max(p => p.x),
                positions.Max(p => p.y),
                positions.Max(p => p.z)
            );

            return (minBounds, maxBounds);
        }

        private static List<Vector3> GenerateQueryPoints(Vector3 minPoint, Vector3 maxPoint, float spatialDelta)
        {
            List<Vector3> queryPoints = new();
            for (float x = minPoint.x; x < maxPoint.x; x += spatialDelta)
            {
                for (float y = minPoint.y; y < maxPoint.y; y += spatialDelta)
                {
                    for (float z = minPoint.z; z < maxPoint.z; z += spatialDelta)
                    {
                        queryPoints.Add(new Vector3(x, y, z));
                    }
                }
            }
            return queryPoints;
        }

        private static List<Vector3> FilterQueryPointsByDistance(List<Vector3> queryPoints, List<Vector3> data, float bandwidth)
        {
            ConcurrentBag<Vector3> filteredQueryPoints = new ConcurrentBag<Vector3>();

            Parallel.ForEach(queryPoints, queryPoint =>
            {
                float minDistance = float.MaxValue;
                foreach (Vector3 dataPoint in data)
                {
                    float distance = Vector3.Distance(queryPoint, dataPoint);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }
                if (minDistance < bandwidth)
                {
                    filteredQueryPoints.Add(queryPoint);
                }
            });

            return filteredQueryPoints.ToList();
        }

        private static Gradient GradientPerTrajectory(Color color)
        {
            // Should be transparent at the beginning and opaque, with color
            // `color` at the end.
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(color, 0.0f),
                    new GradientColorKey(color, 1.0f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 1.0f)
                }
            );
            return gradient;
        }

        public static (List<Vector3>, List<Color>) GenerateDensityHeatmap(
            Dictionary<string, Trajectory> trajectories,
            List<Color> trajectoryColors,
            float spatialDelta,
            float bandwidth,
            float densityThreshold = 0.1f
        )
        {
            (Vector3 minPoint, Vector3 maxPoint) = GetBounds(trajectories);
            List<Vector3> queryPoints = GenerateQueryPoints(minPoint, maxPoint, spatialDelta);
            Debug.Log($"Number of query points before filtering: {queryPoints.Count}");
            List<Vector3> allPositions = trajectories.SelectMany(entry => entry.Value.Select(e => e.Position)).ToList();
            List<Vector3> filteredQueryPoints = FilterQueryPointsByDistance(queryPoints, allPositions, bandwidth);
            Debug.Log($"Number of query points after filtering: {filteredQueryPoints.Count}");
            List<Vector3> particlePoints = new();
            List<Color> colors = new();
            int trajectoryIndex = 0;
            foreach (KeyValuePair<string, Trajectory> entry in trajectories)
            {
                List<Vector3> data = entry.Value.Select(e => e.Position).ToList();
                List<float> densities = KernelDensityEstimate.Evaluate(data, filteredQueryPoints, bandwidth);

                // Remove points with density below threshold.
                List<Vector3> highDensityQueryPoints = new();
                List<float> filteredDensities = new();
                for (int i = 0; i < densities.Count; i++)
                {
                    if (densities[i] > densityThreshold)
                    {
                        highDensityQueryPoints.Add(filteredQueryPoints[i]);
                        filteredDensities.Add(densities[i]);
                    }
                }

                particlePoints.AddRange(highDensityQueryPoints);

                // Create color per sample
                Gradient gradient = GradientPerTrajectory(trajectoryColors[trajectoryIndex % trajectoryColors.Count]);

                for (int i = 0; i < filteredDensities.Count; i++)
                {
                    colors.Add(gradient.Evaluate(filteredDensities[i]));
                }

                trajectoryIndex++;
            }
            return (particlePoints, colors);
        }
    }
}
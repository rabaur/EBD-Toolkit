﻿using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.AI;
using System.Linq;
using EBD;
using System.Globalization;
using Trajectory = System.Collections.Generic.List<EBD.TrajectoryEntry>;
using System;

public class ProcessWalkthrough : MonoBehaviour
{
    // Public variables.
    public LayerMask layerMask;
    public Gradient heatmapGradient;

    // Public variables concerned with the raycast.
    public float horizontalViewAngle = 90.0f;
    public float verticalViewAngle = 60.0f;
    public int numRaysPerRayCast = 100;
    public int maxNumRays = 1000;
    public int numRayCast = 0;

    // Private variables concerned with the raycast.
    private float outerConeRadiusHorizontal;
    private float outerConeRadiusVertical;

    // Visualization-related public variables.
    public float particleSize = 1.0f;
    public float kernelSize = 1.0f;

    public bool useAllFilesInDirectory = false;
    public string rawDataDirectory = DefaultPaths.RawDataPath;
    public string rawDataFileName = Path.Combine("Data", "VirtualWalkthrough", "Raw", "Walkthrough.csv");
    public string outProcessedDataFileName;
    public string outSummarizedDataFileName;
    public string inProcessedDataFileName;
    private List<float> kdeValues;
    private Dictionary<string, int[]> hitsPerLayer;

    // Whether the visual attention heatmap should be computed.
    public bool isVisualAttentionEnabled = false;

    // Whether the trajectories should be visualized.
    public bool isTrajectoryVisEnabled = false;

    // Whether the position heatmap should be computed (mutually exclusive with showVisualAttention).
    public bool isDensityHeatmapEnabled = false;
    public float densityHeatmapDelta = 1f;
    public SerializableColorList densityHeatmapColors = new SerializableColorList();
    private List<Vector3> particlePositions;
    public bool singleColorPerTrajectory = false;
    public SerializableColorList trajectoryColors = new SerializableColorList();
    public Gradient trajectoryGradient;
    public Gradient shortestPathGradient;
    public bool isShortestPathVisEnabled = false;
    public bool inferStartLocation = true;
    public bool inferEndLocation = true;
    public Transform startLocation;
    public Transform endLocation;

    // Key:
    // If not multipleTrials in one file, it is the filename of the raw data file.
    // If multipleTrials in one file, it is the key created from concatenating the values of the key-columns.
    private Dictionary<string, Trajectory> trajectories = new();
    public bool reuseHeatmap = false;
    public float pathWidth = 0.1f;
    private GameObject lineRendererParent;
    private Dictionary<string, LineRenderer> lineRenderer = new();
    private GameObject shortestPathLinerendererParent;
    private LineRenderer shortestPathLinerenderer;
    public Material lineRendererMaterial;
    public Material heatmapMaterial;
    private List<string> rawDataFileNames;
    public string csvDelimiter = ",";
    public bool isDataSummaryEnabled;
    private readonly string outputNumberFormat = "F3";
    public bool showTrajectoryProgressively = false;
    public float replayDuration = 10.0f;
    public bool useQuaternion = false;

    // Column names of the file to be parsed.
    public string positionXColumnName = "PositionX";
    public string positionYColumnName = "PositionY";
    public string positionZColumnName = "PositionZ";
    public string directionXColumnName = "DirectionX";
    public string directionYColumnName = "DirectionY";
    public string directionZColumnName = "DirectionZ";
    public string upXColumnName = "UpX";
    public string upYColumnName = "UpY";
    public string upZColumnName = "UpZ";
    public string rightXColumnName = "RightX";
    public string rightYColumnName = "RightY";
    public string rightZColumnName = "RightZ";
    public string timeColumnName = "Time";
    public string quaternionWColumnName = "QuaternionW";
    public string quaternionXColumnName = "QuaternionX";
    public string quaternionYColumnName = "QuaternionY";
    public string quaternionZColumnName = "QuaternionZ";
    public bool multipleTrialsInOneFile = false;

    // This list contains the names of columns that constitute the key of a trial.
    public SerializableStringList keyColumns = new();

    // This list contains the names of columns that are used for filtering the data.
    public List<SerializableStringList> filters = new();
    private Dictionary<string, List<string>> filterDict = new();

    void Start()
    {
        ValidateInputs();
        Initialize();
        ReadData();
        if (isVisualAttentionEnabled)
        {
            VisualizeAttention();
        }
        if (isDensityHeatmapEnabled)
        {
            VisualizeDensityHeatmap();
        }
        if (isTrajectoryVisEnabled)
        {
            VisualizeTrajectory();
        }
        if (isDataSummaryEnabled)
        {
            WriteSummarizedDataFile();
        }
    }

    void Update()
    {
        if (!isTrajectoryVisEnabled || !showTrajectoryProgressively)
        {
            return;
        }
        int trajectoryIndex = 0;
        foreach (KeyValuePair<string, Trajectory> entry in trajectories)
        {
            List<Vector3> currPositions = entry.Value.Select(x => x.Position).ToList();
            List<float> currTimes = entry.Value.Select(x => x.TimeStamp).ToList();
            Visualization.RenderTrajectory(
                lineRenderer: lineRenderer[entry.Key],
                positions: currPositions.ToList(),
                timesteps: Enumerable.Range(0, currPositions.Count()).Select(i => (float)i).ToList(),
                currentTimeStep: Time.realtimeSinceStartup % replayDuration / replayDuration,
                gradient: singleColorPerTrajectory ? null : trajectoryGradient,
                color: singleColorPerTrajectory ? trajectoryColors.List[trajectoryIndex % trajectoryColors.List.Count] : default,
                trajectoryWidth: pathWidth,
                normalizeTime: true
            );
            trajectoryIndex++;
        }
    }

    void ValidateInputs()
    {
        if (useAllFilesInDirectory && multipleTrialsInOneFile)
        {
            throw new Exception("Using multiple files and multiple trials in one file is not supported.");
        }
        // The total number of rays cannot be smaller than the number of rays per raycast.
        if (numRaysPerRayCast > maxNumRays)
        {
            throw new Exception("numRaysPerRayCast must be smaller than maxNumRays.");
        }

        foreach (SerializableStringList filter in filters)
        {
            if (filter.List.Count < 2)
            {
                Debug.LogError("Each filter must have at least two elements. First element is the column name, the rest are the values to be filtered.");
            }
            filterDict.Add(filter.List[0], filter.List.Skip(1).ToList());
        }

        // Check that only one of showVisualAttention and showPositionHeatmap is set to true.
        if (isVisualAttentionEnabled && isDensityHeatmapEnabled)
        {
            Debug.LogError("Only one of showVisualAttention and showPositionHeatmap can be set to true.");
        }
    }

    void Initialize()
    {
        numRayCast = Mathf.CeilToInt((float)maxNumRays / numRaysPerRayCast);

        if (lineRendererMaterial == null)
        {
            lineRendererMaterial = new Material(Shader.Find("Sprites/Default"));  // Default material for linerenderer.
        }
        if (heatmapMaterial == null)
        {
            heatmapMaterial = new Material(Shader.Find("Particles/Priority Additive (Soft)")); // Default material for heatmap.
        }

        // Initialize hitsPerLayer.
        hitsPerLayer = new();

        // Set material of particle system.
        gameObject.GetComponent<ParticleSystemRenderer>().material = heatmapMaterial;
        outerConeRadiusHorizontal = Mathf.Tan(horizontalViewAngle / 2.0f * Mathf.Deg2Rad);
        outerConeRadiusVertical = Mathf.Tan(verticalViewAngle / 2.0f * Mathf.Deg2Rad);
    }

    void ReadData()
    {
        // Create a list of filenames for the raw data files to be read. If `useAllFilesInDirectory` is false, then this
        // list will consist of only one file. Otherwise all files in that directory will be added.
        rawDataFileNames = new List<string>();
        if (useAllFilesInDirectory)
        {
            // Read in all files in the directory.
            rawDataFileNames = new List<string>(Directory.GetFiles(rawDataDirectory, "*.csv"));
        }
        else
        {
            // Only get single file.
            rawDataFileNames.Add(rawDataFileName);
        }

        // Parse each file and populate the positions and direction arrays.
        foreach (string fileName in rawDataFileNames)
        {
            (List<string> columnNames, List<List<string>> data) = IO.ReadCSV(fileName, separator: csvDelimiter);

            // Filter data.
            data = FilterData(columnNames, data, filters.Select(x => (x.List.First(), x.List.Skip(1).ToList())).ToList());

            // Check that all required columns are present.
            CheckColumns(columnNames);

            // Parse the data and populate the positions and direction arrays.
            foreach (List<string> row in data)
            {
                // Create the key.
                string key = multipleTrialsInOneFile ? CreateSuperKey(keyColumns.List, columnNames, row) : Path.GetFileName(fileName);
                ParseRow(row, key, ref trajectories, columnNames);
            }
        }
    }

    void VisualizeAttention()
    {
        if (reuseHeatmap)
        {
            LoadHeatMap();
        }
        else
        {
            (particlePositions, kdeValues, hitsPerLayer) = VisualAttention.CreateHeatMap(
                trajectories,
                maxNumRays,
                outerConeRadiusVertical,
                outerConeRadiusHorizontal,
                numRaysPerRayCast,
                layerMask,
                kernelSize
            );
            WriteProcessedDataFile();
        }
        ParticleSystem particleSystem = GetComponent<ParticleSystem>();
        Visualization.SetupParticleSystem(particleSystem, particlePositions, kdeValues, heatmapGradient, particleSize);
    }

    void VisualizeDensityHeatmap()
    {
        List<Color> outColors;
        (particlePositions, outColors) = DensityHeatmap.GenerateDensityHeatmap(
            trajectories,
            densityHeatmapColors.List,
            densityHeatmapDelta,
            kernelSize
        );
        ParticleSystem particleSystem = GetComponent<ParticleSystem>();
        Visualization.SetupParticleSystem(particleSystem, particlePositions, outColors, particleSize);
    }

    void VisualizeTrajectory()
    {
        int trajectoryIndex = 0;
        foreach (KeyValuePair<string, Trajectory> entry in trajectories)
        {
            List<Vector3> currPositions = entry.Value.Select(x => x.Position).ToList();
            List<float> currTimes = entry.Value.Select(x => x.TimeStamp).ToList();
            lineRendererParent = new GameObject { hideFlags = HideFlags.HideInHierarchy };
            lineRenderer.Add(entry.Key, lineRendererParent.AddComponent<LineRenderer>());
            Visualization.RenderTrajectory(
                lineRenderer: lineRenderer[entry.Key],
                positions: currPositions.ToList(),
                timesteps: Enumerable.Range(0, currPositions.Count()).Select(i => (float)i).ToList(),
                currentTimeStep: 1.0f,
                gradient: singleColorPerTrajectory ? null : trajectoryGradient,
                color: singleColorPerTrajectory ? trajectoryColors.List[trajectoryIndex % trajectoryColors.List.Count] : default,
                trajectoryWidth: pathWidth,
                normalizeTime: true,
                normalizePosition: false
            );
            trajectoryIndex++;
            if (isShortestPathVisEnabled)
            {
                Vector3 startPos = inferStartLocation ? currPositions.First() : startLocation.position;
                Vector3 endPos = inferEndLocation ? currPositions.Last() : endLocation.position;

                // startPos and endPos do not necessarily lie on the NavMesh. Finding path between them might fail.
                NavMesh.SamplePosition(startPos, out NavMeshHit startHit, 100.0f, NavMesh.AllAreas);  // Hardcoded to 100 units of maximal distance.
                startPos = startHit.position;
                NavMesh.SamplePosition(endPos, out NavMeshHit endHit, 100.0f, NavMesh.AllAreas);
                endPos = endHit.position;

                // Creating linerenderer for shortest path.
                shortestPathLinerendererParent = new GameObject { hideFlags = HideFlags.HideInHierarchy };
                shortestPathLinerenderer = shortestPathLinerendererParent.AddComponent<LineRenderer>();

                // Create shortest path.
                NavMeshPath navMeshPath = new NavMeshPath();
                bool foundPath = NavMesh.CalculatePath(startPos, endPos, NavMesh.AllAreas, navMeshPath);
                if (!foundPath)
                {
                    Debug.LogError("Shortest path could not be calculated. Have you baked the NavMesh?");
                }
                else
                {
                    Visualization.RenderTrajectory(
                        lineRenderer: shortestPathLinerenderer,
                        positions: navMeshPath.corners.ToList(),
                        timesteps: Enumerable.Range(0, navMeshPath.corners.Length).Select(i => (float)i).ToList(),
                        currentTimeStep: 1.0f,
                        gradient: shortestPathGradient,
                        trajectoryWidth: pathWidth,
                        normalizeTime: true
                    );
                }
            }
        }
    }

    // This function is probably defunct.
    private void LoadHeatMap()
    {
        // Reading in the heatmap-data from prior processing and creating arrays for positions and colors \in [0, 1].
        string[] allLines = File.ReadAllLines(inProcessedDataFileName);
        particlePositions = new();
        kdeValues = new List<float>();
        for (int i = 0; i < allLines.Length; i++)
        {
            string[] line = allLines[i].Split(csvDelimiter);
            particlePositions.Add(new Vector3(float.Parse(line[0]), float.Parse(line[1]), float.Parse(line[2])));
            kdeValues.Add(float.Parse(line[3]));
        }
    }

    private void WriteProcessedDataFile()
    {
        using (StreamWriter processedDataFile = new StreamWriter(outProcessedDataFileName))
        {
            for (int i = 0; i < particlePositions.Count; i++)
            {
                processedDataFile.WriteLine(particlePositions[i].x + csvDelimiter + particlePositions[i].y + csvDelimiter + particlePositions[i].z + csvDelimiter + kdeValues[i]);
            }
        }
    }

    private void WriteSummarizedDataFile()
    {
        Debug.Log("Writing summarized data file.");
        // Variables to be written out. One entry per trial id (or file name).
        Dictionary<string, float> durations = new();
        Dictionary<string, float> distances = new();
        Dictionary<string, float> averageSpeeds = new();
        Dictionary<string, float> shortestPathDistances = new();
        Dictionary<string, float> surplusShortestPaths = new();
        Dictionary<string, float> ratioShortestPaths = new();
        Dictionary<string, int> successfuls = new();

        foreach (KeyValuePair<string, Trajectory> entry in trajectories)
        {
            Trajectory currTrajectory = entry.Value;

            // Duration of a walkthrough is the temporal difference between the last update step and the first.
            durations.Add(entry.Key, currTrajectory.Last().TimeStamp - currTrajectory.First().TimeStamp);

            // Add up distances between measures time-points. Note that the resolution at which the time-points are 
            // recorded will make a difference.
            float currDistance = 0.0f;
            for (int j = 0; j < currTrajectory.Count - 1; j++)
            {
                currDistance += Vector3.Distance(currTrajectory[j].Position, currTrajectory[j + 1].Position);
            }
            distances.Add(entry.Key, currDistance);

            averageSpeeds.Add(entry.Key, distances[entry.Key] / durations[entry.Key]);

            Vector3 startPos = inferStartLocation ? currTrajectory.First().Position : startLocation.position;
            Vector3 endPos = inferEndLocation ? currTrajectory.Last().Position : endLocation.position;

            // startPos and endPos do not necessarily lie on the NavMesh. Finding path between them might fail.
            NavMesh.SamplePosition(startPos, out NavMeshHit startHit, 100.0f, NavMesh.AllAreas);  // Hardcoded to 100 units of maximal distance.
            startPos = startHit.position;
            NavMesh.SamplePosition(endPos, out NavMeshHit endHit, 100.0f, NavMesh.AllAreas);
            endPos = endHit.position;

            // Create shortest path.
            NavMeshPath navMeshPath = new NavMeshPath();
            NavMesh.CalculatePath(startPos, endPos, NavMesh.AllAreas, navMeshPath);

            float currShortestPathDistance = 0.0f;
            for (int j = 0; j < navMeshPath.corners.Length - 1; j++)
            {
                currShortestPathDistance += Vector3.Distance(navMeshPath.corners[j], navMeshPath.corners[j + 1]);
            }

            shortestPathDistances.Add(entry.Key, currShortestPathDistance);

            surplusShortestPaths.Add(entry.Key, distances[entry.Key] - shortestPathDistances[entry.Key]);

            ratioShortestPaths.Add(entry.Key, distances[entry.Key] / shortestPathDistances[entry.Key]);

            if (Vector3.Distance(currTrajectory.Last().Position, endPos) < 2.0f)
            {
                successfuls.Add(entry.Key, 1);
            }
            else
            {
                successfuls.Add(entry.Key, 0);
            }
        }
        List<string> columnNames = new();
        if (multipleTrialsInOneFile)
        {
            columnNames.AddRange(keyColumns.List);
        }
        else
        {
            columnNames.Add("TrialID");
        }
        columnNames.AddRange(new List<string> {
                "Duration",
                "Distance",
                "AverageSpeed",
                "ShortestPathDistance",
                "SurplusShortestPath",
                "RatioShortestPath",
                "Successful"
        });

        // Find names of all the Unity layers.
        for (int i = 0; i < 32; i++)
        {
            columnNames.Add(LayerMask.LayerToName(i));
        }

        List<List<string>> data = new();
        Dictionary<string, int> totalHitsPerLayer = new();
        foreach (KeyValuePair<string, int[]> entry in hitsPerLayer)
        {
            totalHitsPerLayer[entry.Key] = isVisualAttentionEnabled ? entry.Value.Sum() : -1;
        }
        foreach (KeyValuePair<string, float> entry in durations)
        {
            List<string> row = new();
            if (multipleTrialsInOneFile)
            {
                row.AddRange(DeconstructSuperKey(entry.Key).Item2);
            }
            else
            {
                row.Add(entry.Key);
            }
            row.AddRange(new List<string> {
                durations[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture),
                distances[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture),
                averageSpeeds[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture),
                shortestPathDistances[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture),
                surplusShortestPaths[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture),
                ratioShortestPaths[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture),
                successfuls[entry.Key].ToString(outputNumberFormat, CultureInfo.InvariantCulture)
            });

            for (int j = 0; j < 32; j++)
            {
                if (isVisualAttentionEnabled)
                {
                    int numHits = hitsPerLayer[entry.Key][j];
                    int numTotalHits = totalHitsPerLayer[entry.Key];
                    row.Add(((float)numHits / numTotalHits).ToString(outputNumberFormat, CultureInfo.InvariantCulture));
                }
                else
                {
                    row.Add("not computed");
                }
            }
            data.Add(row);
        }
        IO.WriteCSV(outSummarizedDataFileName, columnNames, data, csvDelimiter);
    }

    private void ParseRow(
        List<string> row,
        string key,
        ref Dictionary<string, Trajectory> trajectories,
        List<string> columnNames
    )
    {

        // If we have not seen the trial before, we create a new trajectroy.
        if (!trajectories.ContainsKey(key))
        {
            trajectories.Add(key, new Trajectory());
        }

        // Construct a new trajectory entry.
        float currTime = float.Parse(row[columnNames.IndexOf(timeColumnName)], CultureInfo.InvariantCulture);
        Vector3 currPosition = new(
            float.Parse(row[columnNames.IndexOf(positionXColumnName)], CultureInfo.InvariantCulture),
            float.Parse(row[columnNames.IndexOf(positionYColumnName)], CultureInfo.InvariantCulture),
            float.Parse(row[columnNames.IndexOf(positionZColumnName)], CultureInfo.InvariantCulture)
        );
        Vector3 currForwardDirection;
        Vector3 currUpDirection;
        Vector3 currRightDirection;
        if (useQuaternion)
        {
            Quaternion currQuaternion = new(
                float.Parse(row[columnNames.IndexOf(quaternionWColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(quaternionXColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(quaternionYColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(quaternionZColumnName)], CultureInfo.InvariantCulture)
            );
            currForwardDirection = currQuaternion * Vector3.forward;
            currUpDirection = currQuaternion * Vector3.up;
            currRightDirection = currQuaternion * Vector3.right;
        }
        else
        {
            currForwardDirection = new Vector3(
                float.Parse(row[columnNames.IndexOf(directionXColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(directionYColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(directionZColumnName)], CultureInfo.InvariantCulture)
            );
            currUpDirection = new Vector3(
                float.Parse(row[columnNames.IndexOf(upXColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(upYColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(upZColumnName)], CultureInfo.InvariantCulture)
            );
            currRightDirection = new Vector3(
                float.Parse(row[columnNames.IndexOf(rightXColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(rightYColumnName)], CultureInfo.InvariantCulture),
                float.Parse(row[columnNames.IndexOf(rightZColumnName)], CultureInfo.InvariantCulture)
            );
        }

        // Add the new entry to the trajectory.
        trajectories[key].Add(new TrajectoryEntry
        {
            Position = currPosition,
            ForwardDirection = currForwardDirection,
            UpDirection = currUpDirection,
            RightDirection = currRightDirection,
            TimeStamp = currTime
        });
    }

    private void CheckColumns(List<string> columns)
    {
        List<string> requiredColumns = new() {
            positionXColumnName,
            positionYColumnName,
            positionZColumnName,
            timeColumnName,
        };

        if (useQuaternion)
        {
            requiredColumns.AddRange(new List<string> {
                quaternionWColumnName,
                quaternionXColumnName,
                quaternionYColumnName,
                quaternionZColumnName
            });
        }
        else
        {
            requiredColumns.AddRange(new List<string> {
                directionXColumnName,
                directionYColumnName,
                directionZColumnName,
                upXColumnName,
                upYColumnName,
                upZColumnName,
                rightXColumnName,
                rightYColumnName,
                rightZColumnName
            });
        }

        if (multipleTrialsInOneFile)
        {
            requiredColumns.AddRange(keyColumns.List);
        }

        foreach (string requiredColumn in requiredColumns)
        {
            if (!columns.Contains(requiredColumn))
            {
                throw new Exception($"Column {requiredColumn} not found in data file. Possible columns are: {string.Join(", ", columns)}");
            }
        }
    }

    private List<List<string>> FilterData(
        List<string> columnNames,
        List<List<string>> data,
        List<(string, List<string>)> filters
    )
    {
        List<List<string>> filteredData = new();

        // Go through each row and check if the row satisfies all filters.
        foreach (List<string> row in data)
        {
            bool satisfiesAllFilters = true;
            foreach ((string columnName, List<string> filterValues) in filters)
            {
                if (!filterValues.Contains(row[columnNames.IndexOf(columnName)]))
                {
                    satisfiesAllFilters = false;
                    break;
                }
            }
            if (satisfiesAllFilters)
            {
                filteredData.Add(row);
            }
        }
        return filteredData;
    }

    private string CreateSuperKey(
        List<string> keyColumns,
        List<string> columnNames,
        List<string> row,
        char separator = ':'
    )
    {
        List<string> keyValues = keyColumns.Select(x => row[columnNames.IndexOf(x)]).ToList();
        List<string> superKeyComps = keyValues.Zip(keyColumns, (x, y) => $"{y}={x}").ToList();
        return string.Join(separator, superKeyComps);
    }

    private (List<string>, List<string>) DeconstructSuperKey(string superKey, char separator = ':')
    {
        List<string> keyComponents = superKey.Split(separator).ToList();
        List<string> keyColumns = keyComponents.Select(x => x.Split("=")[0]).ToList();
        List<string> keyValues = keyComponents.Select(x => x.Split("=")[1]).ToList();
        return (keyColumns, keyValues);
    }
}

[Serializable]
public class SerializableStringList
{
    public List<string> List;
}

[Serializable]
public class SerializableColorList
{
    public List<Color> List;
}
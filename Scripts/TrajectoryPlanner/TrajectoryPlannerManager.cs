using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TrajectoryPlannerManager : MonoBehaviour
{
    [SerializeField] private CCFModelControl modelControl;
    [SerializeField] private List<GameObject> probePrefabs;
    [SerializeField] private List<int> probePrefabIDs;
    [SerializeField] private Transform brainModel;
    [SerializeField] private Utils util;
    [SerializeField] private TP_RecRegionSlider recRegionSlider;
    [SerializeField] private Collider ccfCollider;
    [SerializeField] private TP_InPlaneSlice inPlaneSlice;
    [SerializeField] private TP_Search searchControl;

    [SerializeField] private TP_PlayerPrefs localPrefs;

    // Which acronym/area name set to use
    [SerializeField] TMP_Dropdown annotationAcronymDropdown;

    private ProbeController activeProbeController;

    private List<ProbeController> allProbes;
    private List<Collider> inactiveProbeColliders;
    private List<Collider> allProbeColliders;
    private List<Collider> rigColliders;
    private List<Collider> allNonActiveColliders;

    Color[] probeColors;

    // Values
    [SerializeField] private int probePanelAcronymTextFontSize = 14;
    [SerializeField] private int probePanelAreaTextFontSize = 10;

    // Coord data
    private Vector3 centerOffset = new Vector3(-5.7f, -4.0f, +6.6f);

    // Manual coordinate entry
    [SerializeField] private TP_CoordinateEntryPanel manualCoordinatePanel;

    // Track who got clicked on, probe, camera, or brain
    public bool ProbeControl { get; set; }
    public bool CameraControl { get; set; }
    public bool BrainControl { get; set; }

    // Track when brain areas get clicked on
    private List<int> targetedBrainAreas;

    // Annotations
    // this file stores the indexes that we actually have data for
    private string datasetIndexFile = "data_indexes";
    // annotation files
    private string annotationIndexFile = "ann/indexes";
    private AnnotationDataset annotationDataset;

    private bool movedThisFrame;
    private bool movedThisFrameDirty;
    private bool spawnedThisFrame = false;

    private int visibleProbePanels;

    private void Awake()
    {
        ProbeControl = false;
        CameraControl = false;
        BrainControl = false;

        visibleProbePanels = 0;

        allProbes = new List<ProbeController>();
        allProbeColliders = new List<Collider>();
        inactiveProbeColliders = new List<Collider>();
        rigColliders = new List<Collider>();
        allNonActiveColliders = new List<Collider>();
        targetedBrainAreas = new List<int>();
        //Physics.autoSyncTransforms = true;

        probeColors = new Color[20] { ColorFromRGB(114, 87, 242), ColorFromRGB(240, 144, 96), ColorFromRGB(71, 147, 240), ColorFromRGB(240, 217, 48), ColorFromRGB(60, 240, 227),
                                    ColorFromRGB(180, 0, 0), ColorFromRGB(0, 180, 0), ColorFromRGB(0, 0, 180), ColorFromRGB(180, 180, 0), ColorFromRGB(0, 180, 180),
                                    ColorFromRGB(180, 0, 180), ColorFromRGB(240, 144, 96), ColorFromRGB(71, 147, 240), ColorFromRGB(240, 217, 48), ColorFromRGB(60, 240, 227),
                                    ColorFromRGB(114, 87, 242), ColorFromRGB(255, 255, 255), ColorFromRGB(0, 125, 125), ColorFromRGB(125, 0, 125), ColorFromRGB(125, 125, 0)};

        modelControl.LateStart(true);

        // First load the indexing file
        Debug.Log("Loading the CCF index file");
        byte[] ccfIndexMap = util.LoadBinaryByteHelper(datasetIndexFile);
        // Load the annotation file
        Debug.Log("Loading the CCF annotation index and map files");
        ushort[] annData = util.LoadBinaryUShortHelper(annotationIndexFile);
        uint[] annMap = util.LoadBinaryUInt32Helper(annotationIndexFile + "_map");
        Debug.Log("Creating the CCF AnnotationDataset object");
        annotationDataset = new AnnotationDataset("annotation", annData, annMap, ccfIndexMap);
    }

    private void Start()
    {
    }

    public void ClickSearchArea(GameObject target)
    {
        searchControl.ClickArea(target);
    }

    public void ToggleBeryl(int value)
    {
        switch (value)
        {
            case 0:
                modelControl.SetBeryl(false);
                break;
            case 1:
                modelControl.SetBeryl(true);
                break;
            default:
                modelControl.SetBeryl(false);
                break;
        }
        foreach (ProbeController probeController in allProbes)
            foreach (ProbeUIManager puimanager in probeController.GetComponents<ProbeUIManager>())
                puimanager.ProbeMoved();
    }

    public Collider CCFCollider()
    {
        return ccfCollider;
    }

    public int ProbePanelTextFS(bool acronym)
    {
        return acronym ? probePanelAcronymTextFontSize : probePanelAreaTextFontSize;
    }
    
    public Vector3 GetCenterOffset()
    {
        return centerOffset;
    }

    public AnnotationDataset GetAnnotationDataset()
    {
        return annotationDataset;
    }

    public int GetActiveProbeType()
    {
        return activeProbeController.GetProbeType();
    }

    // Update is called once per frame
    void Update()
    {
        movedThisFrame = false;

        if (spawnedThisFrame)
        {
            spawnedThisFrame = false;
            return;
        }

        if (Input.anyKey && activeProbeController != null)
        {
            if (Input.GetKeyDown(KeyCode.Backspace) && !manualCoordinatePanel.gameObject.activeSelf)
            {
                DestroyActiveProbeController();
                return;
            }

            if (Input.GetKeyDown(KeyCode.M))
            {
                manualCoordinatePanel.gameObject.SetActive(!manualCoordinatePanel.gameObject.activeSelf);
                if (manualCoordinatePanel.gameObject.activeSelf)
                    manualCoordinatePanel.SetTextValues(activeProbeController);
            }

            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(2))
                movedThisFrame = localPrefs.GetCollisions() ? activeProbeController.MoveProbe(allNonActiveColliders) : activeProbeController.MoveProbe(new List<Collider>());

            if (movedThisFrame)
                inPlaneSlice.UpdateInPlaneSlice();
        }
    }

    private void DestroyActiveProbeController()
    {
        activeProbeController.Destroy();
        Destroy(activeProbeController.gameObject);
        allProbes.Remove(activeProbeController);
        if (allProbes.Count > 0)
            SetActiveProbe(allProbes[0]);
        else
            activeProbeController = null;
    }

    public void ManualCoordinateEntry(float ap, float ml, float depth, float phi, float theta, float spin)
    {
        activeProbeController.ManualCoordinateEntry(ap, ml, depth, phi, theta, spin);
    }

    public void AddIBLProbes()
    {
        // Add two probes to the scene, one coming from the left and one coming from the right
        StartCoroutine(DelayedIBLProbeAdd(0, 45, 0f));
        StartCoroutine(DelayedIBLProbeAdd(180, 45, 0.2f));
    }

    IEnumerator DelayedIBLProbeAdd(float phi, float theta, float delay)
    {
        yield return new WaitForSeconds(delay);
        AddNewProbe(1);
        yield return new WaitForSeconds(0.05f);
        activeProbeController.SetProbePosition(0, 0, 0, phi, theta, 0);
    }

    IEnumerator DelayedMoveAllProbes()
    {
        yield return new WaitForSeconds(0.05f);
        MoveAllProbes();
    }

    public void AddNewProbeVoid(int probeType)
    {
        CountProbePanels();
        if (visibleProbePanels >= 16)
            return;

        GameObject newProbe = Instantiate(probePrefabs[probePrefabIDs.FindIndex(x => x == probeType)], brainModel);
        SetActiveProbe(newProbe.GetComponent<ProbeController>());
        if (visibleProbePanels > 4)
            activeProbeController.ResizeProbePanel(700);

        RecalculateProbePanels();

        spawnedThisFrame = true;
        DelayedMoveAllProbes();
    }
    public ProbeController AddNewProbe(int probeType)
    {
        CountProbePanels();
        if (visibleProbePanels >= 16)
            return null;

        GameObject newProbe = Instantiate(probePrefabs[probePrefabIDs.FindIndex(x => x == probeType)], brainModel);
        SetActiveProbe(newProbe.GetComponent<ProbeController>());
        if (visibleProbePanels > 4)
            activeProbeController.ResizeProbePanel(700);

        RecalculateProbePanels();

        spawnedThisFrame = true;
        DelayedMoveAllProbes();

        return newProbe.GetComponent<ProbeController>();
    }

    private void CountProbePanels()
    {
        visibleProbePanels = GameObject.FindGameObjectsWithTag("ProbePanel").Length;
    }

    private void RecalculateProbePanels()
    {
        CountProbePanels();

        if (visibleProbePanels > 8)
        {
            // Increase the layout to have 8 columns and two rows
            GameObject.Find("ProbePanelParent").GetComponent<GridLayoutGroup>().constraintCount = 8;
        }
        else if (visibleProbePanels > 4)
        {
            // Increase the layout to have two rows, by shrinking all the ProbePanel objects to be 500 pixels tall
            GridLayoutGroup probePanelParent = GameObject.Find("ProbePanelParent").GetComponent<GridLayoutGroup>();
            Vector2 cellSize = probePanelParent.cellSize;
            cellSize.y = 700;
            probePanelParent.cellSize = cellSize;

            // now resize all existing probeUIs to be 700 tall
            foreach (ProbeController probeController in allProbes)
            {
                probeController.ResizeProbePanel(700);
            }
        }
    }

    public void RegisterProbe(ProbeController probeController, List<Collider> colliders)
    {
        Debug.Log("Registering probe: " + probeController.gameObject.name);
        allProbes.Add(probeController);
        probeController.RegisterProbeCallback(allProbes.Count, probeColors[allProbes.Count-1]);
        foreach (Collider collider in colliders)
            allProbeColliders.Add(collider);
    }

    public void SetActiveProbe(ProbeController newActiveProbeController)
    {
        Debug.Log("Setting active probe to: " + newActiveProbeController.gameObject.name);
        activeProbeController = newActiveProbeController;

        foreach (ProbeUIManager puimanager in activeProbeController.gameObject.GetComponents<ProbeUIManager>())
            puimanager.ProbeSelected(true);

        foreach (ProbeController pcontroller in allProbes)
            if (pcontroller != activeProbeController)
                foreach (ProbeUIManager puimanager in pcontroller.gameObject.GetComponents<ProbeUIManager>())
                    puimanager.ProbeSelected(false);

        inactiveProbeColliders = new List<Collider>();
        List<Collider> activeProbeColliders = activeProbeController.GetProbeColliders();
        foreach (Collider collider in allProbeColliders)
            if (!activeProbeColliders.Contains(collider))
                inactiveProbeColliders.Add(collider);
        UpdateNonActiveColliders();
        movedThisFrame = true;

        // Also update the recording region size slider
        recRegionSlider.SliderValueChanged(activeProbeController.GetRecordingRegionSize());
    }

    public void ResetActiveProbe()
    {
        activeProbeController.ResetPosition();
    }

    public Color GetProbeColor(int probeID)
    {
        return probeColors[probeID];
    }

    public ProbeController GetActiveProbeController()
    {
        return activeProbeController;
    }

    public bool MovedThisFrame()
    {
        return movedThisFrame;
    }

    public void UpdateInPlaneView()
    {
        inPlaneSlice.UpdateInPlaneSlice();
    }

    public void UpdateRigColliders(List<Collider> newRigColliders, bool keep)
    {
        if (keep)
            foreach (Collider collider in newRigColliders)
                rigColliders.Add(collider);
        else
            foreach (Collider collider in newRigColliders)
                rigColliders.Remove(collider);
        UpdateNonActiveColliders();
    }

    private void UpdateNonActiveColliders()
    {
        allNonActiveColliders.Clear();
        foreach (Collider collider in inactiveProbeColliders)
            allNonActiveColliders.Add(collider);
        foreach (Collider collider in rigColliders)
            allNonActiveColliders.Add(collider);
    }

    private void MoveAllProbes()
    {
        if (activeProbeController != null)
            foreach (ProbeUIManager puimanager in activeProbeController.GetComponents<ProbeUIManager>())
                puimanager.ProbeMoved();
    }


    public void SelectBrainArea(int id)
    {
        if (targetedBrainAreas.Contains(id))
        {
            ClearTargetedBrainArea(id);
            targetedBrainAreas.Remove(id);
        }
        else
        {
            TargetBrainArea(id);
            targetedBrainAreas.Add(id);
        }
    }

    private void TargetBrainArea(int id)
    {
        modelControl.ChangeMaterial(id, "lit");
    }

    private void ClearTargetedBrainArea(int id)
    {
        modelControl.ChangeMaterial(id, "default");
    }


    ///
    /// HELPER FUNCTIONS
    /// 
    public Color ColorFromRGB(int r, int g, int b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    public Vector2 World2IBL(Vector2 phiTheta)
    {
        float iblPhi = -phiTheta.x - 90f;
        float iblTheta = -phiTheta.y;
        return new Vector2(iblPhi, iblTheta);
    }

    public Vector2 IBL2World(Vector2 iblPhiTheta)
    {
        float worldPhi = -iblPhiTheta.x - 90f;
        float worldTheta = -iblPhiTheta.y;
        return new Vector2(worldPhi, worldTheta);
    }

    ///
    /// SETTINGS
    /// 

    public void SetRecordingRegion(bool state)
    {
        localPrefs.SetRecordingRegionOnly(state);
        foreach (ProbeController probeController in allProbes)
            foreach (ProbeUIManager puimanager in probeController.GetComponents<ProbeUIManager>())
                puimanager.ProbeMoved();
    }

    public bool RecordingRegionOnly()
    {
        return localPrefs.GetRecordingRegionOnly();
    }

    public void SetAcronyms(bool state)
    {
        localPrefs.SetAcronyms(state);
        // move probes to update state
        MoveAllProbes();
    }

    public bool UseAcronyms()
    {
        return localPrefs.GetAcronyms();
    }

    public void SetDepth(bool state)
    {
        localPrefs.SetDepthFromBrain(state);
        foreach (ProbeController probeController in allProbes)
            probeController.UpdateText();
    }
    public bool GetDepthFromBrain()
    {
        return localPrefs.GetDepthFromBrain();
    }

    public void SetConvertToProbe(bool state)
    {
        localPrefs.SetAPML2ProbeAxis(state);
        foreach (ProbeController probeController in allProbes)
            probeController.UpdateText();
    }

    public bool GetConvertAPML2Probe()
    {
        return localPrefs.GetAPML2ProbeAxis();
    }

    public void SetCollisions(bool toggleCollisions)
    {
        localPrefs.SetCollisions(toggleCollisions);
    }

    public void SetBregma(bool useBregma)
    {
        localPrefs.SetBregma(useBregma);

        foreach (ProbeController pcontroller in allProbes)
        {
            pcontroller.SetProbePosition();
            pcontroller.UpdateText();
        }
    }

    public void SetInPlane(bool state)
    {
        localPrefs.SetInplane(state);
        inPlaneSlice.UpdateInPlane();
    }

    public bool GetBregma()
    {
        return localPrefs.GetBregma();
    }
}

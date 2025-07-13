﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KModkit;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using wawa.DDL;
using wawa.IO;
using wawa.Modules;
using wawa.Schemas;

[System.Serializable]
public class Extra
{
    public List<string> tags;

    public Extra()
    {
        tags = new List<string>();
    }
}

[System.Serializable]
public class CustomCoordinate
{
    public float heading;
    public float pitch;
    public float zoom;
    public string panoId;
    public Extra extra;
}

[System.Serializable]
public class MapJson
{
    public string name;
    public List<CustomCoordinate> customCoordinates;
}

public class geoGuessrScript : MonoBehaviour
{
    public KMAudio audio;
    public KMBombInfo bomb;

    public KMSelectable openMapButton;
    public KMSelectable guessButton;
    public KMSelectable openStreetViewButton;

    public GameObject streetViewTab;
    public GameObject guessTab;

    public GameObject loadingTab;
    public GameObject loadingBackground;
    public GameObject loadingBackgroundTransparent;
    public GameObject loadingSpinner;

    public Material streetViewMaterial;

    public List<Texture> svTextures = new List<Texture>();

    public GameObject streetViewBlock;
    public KMSelectable streetViewCompass;

    [SerializeField]
    private KMHighlightable streetViewCompassHL;

    public KMSelectable returnToStartButton;

    public KMSelectable zoomInButton;
    public KMSelectable zoomOutButton;

    public KMSelectable panCameraLeftButton;
    public KMSelectable panCameraDownButton;
    public KMSelectable panCameraUpButton;
    public KMSelectable panCameraRightButton;

    private string correctCountryCode;

    public KMSelectable[] letterUpButtons;
    public KMSelectable[] letterDownButtons;
    public TextMesh[] letters;

    public AudioClip[] sounds;

    private static Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
    private string[] locPropertiesCache;
    private string[] startingLocProperties;

    [SerializeField]
    private TextAsset countriesListRaw;

    [SerializeField]
    private TextAsset ktaneManualLocs;

    [SerializeField]
    private TextAsset aawLocs;

    [SerializeField]
    private TextAsset aarwLocs;

    [SerializeField]
    private TextAsset intersectionGuessrLocs;

    private bool isBusy;

    [Serializable]
    public sealed class geoGuessrSettings
    {
        [TweaksSetting.Dropdown(
            "The map of locations that can show up onto the module.",
            "Map",
            "KTaNE Manual",
            "An Arbitrary World",
            "An Arbitrary Rural World",
            "IntersectionGuessr"
        )]
        public object geoGuessrMapUsed { get; set; }

        [TweaksSetting.Checkbox(
            "If enabled, all countries have an equal chance of appearing.",
            "Degenerated distribution"
        )]
        public bool geoGuessrDegenerateLocations { get; set; }

        [TweaksSetting.Dropdown(
            "No Move will let you pan/zoom, while NMPZ will lock all movement.",
            "Game Mode",
            "No Move",
            "NMPZ",
            "Random"
        )]
        public object geoGuessrGamemode { get; set; }

        public geoGuessrSettings()
        {
            this.geoGuessrMapUsed = "KTaNE Manual";
            this.geoGuessrDegenerateLocations = false;
            this.geoGuessrGamemode = "No Move";
        }
    }

    private Config<geoGuessrSettings> moduleSettings;

    static readonly TweaksEditorSettings TweaksEditorSettings = TweaksEditorSettings
        .CreateListing("GeoGuessr", "geoGuessr")
        .Register<geoGuessrSettings>()
        .BuildAndClear();

    char[] alphabet = new char[]
    {
        'A',
        'B',
        'C',
        'D',
        'E',
        'F',
        'G',
        'H',
        'I',
        'J',
        'K',
        'L',
        'M',
        'N',
        'O',
        'P',
        'Q',
        'R',
        'S',
        'T',
        'U',
        'V',
        'W',
        'X',
        'Y',
        'Z',
    };

    private bool movingAllowed = false;
    private bool panningAllowed = true;
    private bool zoomingAllowed = true;

    // Logging
    Dictionary<string, string> countriesList = new Dictionary<string, string>();
    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;

    Material newMaterial;

    void SetHUDByGamemode()
    {
        if (!panningAllowed)
        {
            panCameraUpButton.gameObject.SetActive(false);
            panCameraDownButton.gameObject.SetActive(false);
            panCameraLeftButton.gameObject.SetActive(false);
            panCameraRightButton.gameObject.SetActive(false);
            streetViewCompassHL.gameObject.SetActive(false);
        }
        if (!zoomingAllowed)
        {
            zoomInButton.gameObject.SetActive(false);
            zoomOutButton.gameObject.SetActive(false);
            streetViewCompassHL.gameObject.SetActive(false);
            streetViewCompass.transform.localPosition = new Vector3(-0.06675f, 0.0101f, -0.06675f);
        }
        if (!panningAllowed && !zoomingAllowed)
        {
            returnToStartButton.gameObject.SetActive(false);
        }
    }

    void Awake()
    {
        moduleSettings = new Config<geoGuessrSettings>("geoGuessr-settings.json");

        if (
            moduleSettings.Read().geoGuessrGamemode.ToString() == "NMPZ"
            || (
                moduleSettings.Read().geoGuessrGamemode.ToString() == "Random"
                && UnityEngine.Random.Range(0, 2) == 1
            )
        )
        {
            panningAllowed = false;
            zoomingAllowed = false;
        }

        SetHUDByGamemode();

        newMaterial = new Material(streetViewMaterial);
        streetViewBlock.GetComponent<Renderer>().material = newMaterial;
        moduleId = moduleIdCounter++;

        openMapButton.OnInteract += delegate()
        {
            setToMap();
            return false;
        };
        guessButton.OnInteract += delegate()
        {
            guess(GetCountryCodeInput());
            return false;
        };
        openStreetViewButton.OnInteract += delegate()
        {
            setToStreetView();
            return false;
        };

        streetViewCompass.OnInteract += delegate()
        {
            StartCoroutine(faceNorth());
            return false;
        };
        returnToStartButton.OnInteract += delegate()
        {
            StartCoroutine(returnToStart());
            return false;
        };
        zoomInButton.OnInteract += delegate()
        {
            StartCoroutine(incrementZoom(1));
            return false;
        };
        zoomOutButton.OnInteract += delegate()
        {
            StartCoroutine(incrementZoom(-1));
            return false;
        };
        panCameraLeftButton.OnInteract += delegate()
        {
            StartCoroutine(panCamera(-1, 0));
            return false;
        };
        panCameraDownButton.OnInteract += delegate()
        {
            StartCoroutine(panCamera(0, -1));
            return false;
        };
        panCameraUpButton.OnInteract += delegate()
        {
            StartCoroutine(panCamera(0, 1));
            return false;
        };
        panCameraRightButton.OnInteract += delegate()
        {
            StartCoroutine(panCamera(1, 0));
            return false;
        };

        // Handle letter buttons

        for (int i = 0; i < letters.Length; i++)
        {
            TextMesh letter = letters[i];
            KMSelectable upButton = letterUpButtons[i];
            KMSelectable downButton = letterDownButtons[i];

            upButton.OnInteract += delegate()
            {
                onLetterButtonPressed(letter, -1, upButton);
                return false;
            };
            downButton.OnInteract += delegate()
            {
                onLetterButtonPressed(letter, 1, downButton);
                return false;
            };
        }

        countriesList = JsonConvert.DeserializeObject<Dictionary<string, string>>(
            countriesListRaw.text
        );
    }

    void Update()
    {
        if (loadingTab.activeSelf)
        {
            loadingSpinner.transform.localRotation *= Quaternion.Euler(0, 180f * Time.deltaTime, 0);
        }
        else
        {
            loadingSpinner.transform.localRotation = Quaternion.Euler(0, 180, 0);
        }
    }

    private IEnumerator LoadRandomLocation()
    {
        string mapUsed = moduleSettings.Read().geoGuessrMapUsed.ToString();
        bool isDegenerated = moduleSettings.Read().geoGuessrDegenerateLocations;
        Debug.LogFormat(
            "[GeoGuessr #{0}] Fetching random location from '{1}' map",
            moduleId,
            mapUsed
        );
        List<CustomCoordinate> customCoordinates = new List<CustomCoordinate>();
        if (mapUsed == "An Arbitrary World")
        {
            customCoordinates = JsonConvert.DeserializeObject<List<CustomCoordinate>>(aawLocs.text);
        }
        else if (mapUsed == "An Arbitrary Rural World")
        {
            customCoordinates = JsonConvert.DeserializeObject<List<CustomCoordinate>>(
                aarwLocs.text
            );
        }
        else if (mapUsed == "IntersectionGuessr")
        {
            customCoordinates = JsonConvert.DeserializeObject<List<CustomCoordinate>>(
                intersectionGuessrLocs.text
            );
        }
        else
        {
            MapJson mapJson = JsonConvert.DeserializeObject<MapJson>(ktaneManualLocs.text);
            customCoordinates = mapJson.customCoordinates;
        }

        CustomCoordinate location = customCoordinates[
            UnityEngine.Random.Range(0, customCoordinates.Count)
        ];
        if (isDegenerated)
        {
            Debug.LogFormat(
                "[GeoGuessr #{0}] 'Degenerated Distribution' is turned on | Picking a random country",
                moduleId,
                mapUsed
            );

            List<string> countryCodesList = new List<string>();
            Dictionary<string, List<CustomCoordinate>> countryCodeDictionary =
                new Dictionary<string, List<CustomCoordinate>>();

            foreach (var customCoordinate in customCoordinates)
            {
                if (!countryCodesList.Contains(customCoordinate.extra.tags[0]))
                {
                    countryCodesList.Add(customCoordinate.extra.tags[0]);
                }

                if (!countryCodeDictionary.ContainsKey(customCoordinate.extra.tags[0]))
                {
                    countryCodeDictionary[customCoordinate.extra.tags[0]] =
                        new List<CustomCoordinate>();
                }

                countryCodeDictionary[customCoordinate.extra.tags[0]].Add(customCoordinate);
            }

            string randomCountryCode = countryCodesList[
                UnityEngine.Random.Range(0, countryCodesList.Count)
            ];
            List<CustomCoordinate> countryCoordinates;
            if (countryCodeDictionary.TryGetValue(randomCountryCode, out countryCoordinates))
            {
                if (countryCoordinates.Count > 0)
                {
                    location = countryCoordinates[
                        UnityEngine.Random.Range(0, countryCoordinates.Count)
                    ];
                }
            }
        }

        string[] locProperties = new string[]
        {
            location.extra.tags[0],
            location.panoId,
            location.heading.ToString(),
            location.pitch.ToString(),
            location.zoom.ToString(),
        };
        yield return StartCoroutine(SetLocation(locProperties));
    }

    private IEnumerator SetLocation(string[] locProperties)
    {
        if (isBusy)
        {
            yield break;
        }
        isBusy = true;
        CustomCoordinate location = new CustomCoordinate
        {
            extra = new Extra { tags = { locProperties[0] } },
            panoId = locProperties[1],
            heading = float.Parse(locProperties[2]),
            pitch = float.Parse(locProperties[3]),
            zoom = float.Parse(locProperties[4]),
        };
        float fov = (float)
            Math.Round(3.9018 * Math.Pow(location.zoom, 2) - 42.431 * location.zoom + 123);
        string url = String.Format(
            "https://streetviewpixels-pa.googleapis.com/v1/thumbnail?w=1080&h=1080&panoid={0}&yaw={1}&pitch={2}&cb_client=maps_sv.share&thumbfov={3}",
            location.panoId,
            location.heading,
            location.pitch * -1,
            fov
        );
        Debug.LogFormat("[GeoGuessr #{0}] Downloading image", moduleId);
        yield return StartCoroutine(DownloadTexture(url, locProperties, true));
    }

    private IEnumerator DownloadTexture(
        string url,
        string[] locProperties,
        bool shouldFallbackOnError
    )
    {
        newMaterial.SetColor("_Color", new Color(0.5f, 0.5f, 0.5f));
        setTabVisible(loadingTab, true);
        Texture2D cachedTexture;
        if (textureCache.TryGetValue(url, out cachedTexture))
        {
            newMaterial.mainTexture = cachedTexture;
            Debug.LogFormat("[GeoGuessr #{0}] Loaded cached image", moduleId);
            SetLocationSettings(locProperties, true, null);
            yield break;
        }

        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.isNetworkError || request.isHttpError)
        {
            if (!shouldFallbackOnError)
            {
                isBusy = false;
                newMaterial.SetColor("_Color", new Color(1, 1, 1));
                openMapButton.gameObject.SetActive(true);
                setTabVisible(loadingTab, false);
                loadingBackground.gameObject.SetActive(false);
                loadingBackgroundTransparent.gameObject.SetActive(true);
                yield break;
            }
            Debug.LogFormat(
                "[GeoGuessr #{0}] Could not download panoId {1}; fallbacking to local panorama",
                moduleId,
                locProperties[1]
            );
            panningAllowed = false;
            zoomingAllowed = false;
            SetHUDByGamemode();
            int randomIndex = UnityEngine.Random.Range(0, svTextures.Count);
            Texture randomTexture = svTextures[randomIndex];
            newMaterial.mainTexture = randomTexture;

            string randomTextureName = randomTexture.name;

            Debug.LogFormat(
                "[GeoGuessr #{0}] Loaded panoId {1}: {2}",
                moduleId,
                locProperties[1],
                randomTextureName
            );
            locProperties = randomTextureName.Split('#');
            SetLocationSettings(locProperties, false, null);
            yield break;
        }

        Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
        textureCache[url] = texture;
        newMaterial.mainTexture = texture;
        Debug.LogFormat("[GeoGuessr #{0}] Loaded online image", moduleId);
        SetLocationSettings(locProperties, true, url);
    }

    void SetLocationSettings(string[] locProperties, bool isOnline, string url)
    {
        if (correctCountryCode == null)
        {
            correctCountryCode = locProperties[0];
            Debug.LogFormat(
                "[GeoGuessr #{0}] Correct country: {1} ({2})",
                moduleId,
                correctCountryCode,
                countriesList.ContainsKey(correctCountryCode)
                    ? countriesList[correctCountryCode]
                    : "Invalid country code"
            );
            string googleMapsLink = String.Format(
                "https://www.google.com/maps/@?api=1&map_action=pano&pano={0}&heading={1}&pitch={2}&zoom={3}",
                locProperties[1],
                locProperties[2],
                locProperties[3],
                locProperties[4]
            );

            Debug.LogFormat("[GeoGuessr #{0}] Panorama URL: {1}", moduleId, googleMapsLink);
            if (url != null)
            {
                Debug.LogFormat("[GeoGuessr #{0}] Image link: {1}", moduleId, url);
            }
        }

        if (isOnline || locPropertiesCache == null)
        {
            locPropertiesCache = locProperties;
        }
        if (startingLocProperties == null || startingLocProperties.Length < 5)
        {
            startingLocProperties = locProperties;
        }

        float angle;

        if (float.TryParse(locProperties[2], out angle))
        {
            streetViewCompass.transform.localRotation = Quaternion.Euler(0, 180 - angle, 0);
            streetViewCompassHL.transform.localRotation = Quaternion.Euler(0, -(180 - angle), 0);
        }

        newMaterial.SetColor("_Color", new Color(1, 1, 1));
        openMapButton.gameObject.SetActive(true);
        setTabVisible(loadingTab, false);
        loadingBackground.gameObject.SetActive(false);
        loadingBackgroundTransparent.gameObject.SetActive(true);
        isBusy = false;
    }

    public string GetCountryCodeInput()
    {
        string combinedText = "";

        foreach (TextMesh letter in letters)
        {
            combinedText += letter.text;
        }

        return combinedText;
    }

    void Start()
    {
        setToStreetView();
        openMapButton.gameObject.SetActive(false);
        loadingBackgroundTransparent.gameObject.SetActive(false);
        StartCoroutine(LoadRandomLocation());
    }

    void onLetterButtonPressed(TextMesh letter, int offset, KMSelectable button)
    {
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        char currentLetter = letter.text[0];
        int currentIndex = System.Array.IndexOf(alphabet, currentLetter);

        int newIndex = (currentIndex + offset) % alphabet.Length;

        if (newIndex < 0)
        {
            newIndex += alphabet.Length;
        }

        letter.text = alphabet[newIndex].ToString();
        button.AddInteractionPunch(0.1f);
    }

    private IEnumerator incrementZoom(float amount)
    {
        if (
            locPropertiesCache == null
            || locPropertiesCache.Length < 5
            || isBusy
            || !zoomingAllowed
        )
        {
            yield break;
        }
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        float currentZoom = Mathf.Round(float.Parse(locPropertiesCache[4]));
        float newZoom = Mathf.Clamp((float)currentZoom + amount, 0, 4);
        CustomCoordinate location = new CustomCoordinate
        {
            extra = new Extra { tags = { correctCountryCode } },
            panoId = locPropertiesCache[1],
            heading = float.Parse(locPropertiesCache[2]),
            pitch = float.Parse(locPropertiesCache[3]),
            zoom = newZoom,
        };
        string[] locProperties = new string[]
        {
            location.extra.tags[0],
            location.panoId,
            location.heading.ToString(),
            location.pitch.ToString(),
            location.zoom.ToString(),
        };
        yield return StartCoroutine(SetLocation(locProperties));
    }

    float RoundToNearest(float value, float nearest)
    {
        return Mathf.Round(value / nearest) * nearest;
    }

    private IEnumerator panCamera(float headingIncrement, float yawIncrement)
    {
        if (
            locPropertiesCache == null
            || locPropertiesCache.Length < 5
            || isBusy
            || !panningAllowed
        )
        {
            yield break;
        }
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        float currentZoom = float.Parse(locPropertiesCache[4]);
        float currentHeading = float.Parse(locPropertiesCache[2]);
        float currentYaw = float.Parse(locPropertiesCache[3]);
        float newZoom = Mathf.Clamp(RoundToNearest((float)currentZoom, 1), 0, 4);

        float multiplier = 5f;

        if (newZoom <= 0)
        {
            multiplier = 45f;
        }
        else if (newZoom <= 1)
        {
            multiplier = 45f;
        }
        else if (newZoom <= 2)
        {
            multiplier = 45f;
        }
        else if (newZoom <= 3)
        {
            multiplier = 15f;
        }
        else if (newZoom <= 4)
        {
            multiplier = 5f;
        }

        if (headingIncrement != 0f)
        {
            currentHeading =
                RoundToNearest(currentHeading + (headingIncrement * multiplier), multiplier) % 360;
        }
        else
        {
            currentYaw =
                Mathf.Clamp(
                    RoundToNearest(currentYaw + (yawIncrement * multiplier) + 90, multiplier),
                    0,
                    180
                ) - 90;
        }
        CustomCoordinate location = new CustomCoordinate
        {
            extra = new Extra { tags = { correctCountryCode } },
            panoId = locPropertiesCache[1],
            heading = currentHeading,
            pitch = currentYaw,
            zoom = newZoom,
        };
        string[] locProperties = new string[]
        {
            location.extra.tags[0],
            location.panoId,
            location.heading.ToString(),
            location.pitch.ToString(),
            location.zoom.ToString(),
        };
        yield return StartCoroutine(SetLocation(locProperties));
    }

    private IEnumerator faceNorth()
    {
        if (
            locPropertiesCache == null
            || locPropertiesCache.Length < 5
            || isBusy
            || !panningAllowed
            || !zoomingAllowed
        )
        {
            yield break;
        }
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        CustomCoordinate location = new CustomCoordinate
        {
            extra = new Extra { tags = { correctCountryCode } },
            panoId = locPropertiesCache[1],
            heading = 0, // float.Parse(locPropertiesCache[2]),
            pitch = float.Parse(locPropertiesCache[3]),
            zoom = float.Parse(locPropertiesCache[4]),
        };
        if (locPropertiesCache[2] == "0")
        {
            // Already facing north; face down on the floor
            location.pitch = -90;
            location.zoom = 0;
        }
        string[] locProperties = new string[]
        {
            location.extra.tags[0],
            location.panoId,
            location.heading.ToString(),
            location.pitch.ToString(),
            location.zoom.ToString(),
        };
        yield return StartCoroutine(SetLocation(locProperties));
    }

    private IEnumerator returnToStart()
    {
        if (startingLocProperties == null || startingLocProperties.Length < 5 || isBusy)
        {
            yield break;
        }
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        yield return StartCoroutine(SetLocation(startingLocProperties));
    }

    void guess(string guessedCountryCode)
    {
        Debug.LogFormat(
            "[GeoGuessr #{0}] Guessed: {1} ({2})",
            moduleId,
            guessedCountryCode,
            countriesList.ContainsKey(guessedCountryCode)
                ? countriesList[guessedCountryCode]
                : "Invalid country code"
        );

        guessButton.AddInteractionPunch();
        SoundCore.Play(SoundCore.ToSound(sounds[0]), GetComponent<KMAudio>(), transform);
        setTabVisible(streetViewTab, true);
        setTabVisible(guessTab, false);
        if (guessedCountryCode != correctCountryCode)
        {
            Debug.LogFormat(
                "[GeoGuessr #{0}] Strike! Incorrect guess",
                moduleId,
                correctCountryCode
            );
            GetComponent<KMBombModule>().HandleStrike();
            return;
        }
        if (moduleSolved)
        {
            Debug.LogFormat("[GeoGuessr #{0}] Module already solved", moduleId);
            return;
        }
        Debug.LogFormat("[GeoGuessr #{0}] Module solved", moduleId);
        GetComponent<KMBombModule>().HandlePass();
        moduleSolved = true;
        SoundCore.Play(SoundCore.ToSound(sounds[1]), GetComponent<KMAudio>(), transform);
    }

    void setToMap()
    {
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        openMapButton.AddInteractionPunch(0.1f);
        setTabVisible(streetViewTab, false);
        setTabVisible(guessTab, true);
    }

    void setToStreetView()
    {
        SoundCore.Play(Sound.ButtonPress, GetComponent<KMAudio>(), transform);
        openStreetViewButton.AddInteractionPunch(0.1f);
        setTabVisible(streetViewTab, true);
        setTabVisible(guessTab, false);
        guessButton.AddInteractionPunch(0.1f);
    }

    void setTabVisible(GameObject tab, bool isVisible)
    {
        tab.gameObject.SetActive(isVisible);
    }

    private readonly string TwitchHelpMessage =
        @"Use '!{0} guess <country-code>' to guess. Use '!{0} pan left/down/up/right' to pan the panorama. Use '!{0} zoom in/out' to zoom in or out. Use '!{0} north' to face the panorama north. Use '!{0} return' to return back to the original position.";

    KMSelectable[] ProcessTwitchCommand(string commandRaw)
    {
        if (correctCountryCode == null)
        {
            return null;
        }
        commandRaw = commandRaw.Trim().ToLowerInvariant();
        string[] command = commandRaw.Split(' ');
        if ((command[0] == "guess" || command[0] == "g") && command.Length >= 2)
        {
            for (int i = 0; i < letters.Length; i++)
            {
                TextMesh letter = letters[i];
                string subbedLetter = command[1].ToUpperInvariant().Substring(i, 1);
                if (subbedLetter != null)
                {
                    letter.text = subbedLetter;
                }
            }
            return new[] { guessButton };
        }

        if (panningAllowed && (command[0] == "pan" || command[0] == "p") && command.Length >= 2)
        {
            if (command[1] == "left" || command[1] == "l")
            {
                return new[] { panCameraLeftButton };
            }
            if (command[1] == "down" || command[1] == "d")
            {
                return new[] { panCameraDownButton };
            }
            if (command[1] == "up" || command[1] == "u")
            {
                return new[] { panCameraUpButton };
            }
            if (command[1] == "right" || command[1] == "r")
            {
                return new[] { panCameraRightButton };
            }
        }

        if (zoomingAllowed && (command[0] == "zoom" || command[0] == "z") && command.Length >= 2)
        {
            if (command[1] == "in" || command[1] == "i" || command[1] == "+")
            {
                return new[] { zoomInButton };
            }
            if (command[1] == "out" || command[1] == "o" || command[1] == "-")
            {
                return new[] { zoomOutButton };
            }
        }

        if (
            panningAllowed
            && zoomingAllowed
            && (command[0] == "north" || command[0] == "n" || command[0] == "compass")
        )
        {
            return new[] { streetViewCompass };
        }

        if (
            (panningAllowed || zoomingAllowed)
            && (
                command[0] == "return"
                || command[0] == "restart"
                || command[0] == "reset"
                || command[0] == "r"
            )
        )
        {
            return new[] { returnToStartButton };
        }

        return null;
    }

    void TwitchHandleForcedSolve()
    {
        if (correctCountryCode == null)
        {
            return;
        }
        guess(correctCountryCode);
    }
}

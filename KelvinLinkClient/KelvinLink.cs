using RedLoader;
using RedLoader.Unity.IL2CPP.Utils;
using SonsSdk;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KelvinLinkMod
{
    public class KelvinLinkClient : SonsMod
    {
        private GameObject helperObject;
        private KelvinLinkHelper helperComponent;
        private UnityAction<Scene, LoadSceneMode> sceneLoadedAction;

        protected override void OnInitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod zainicjalizowany");

            // Helper do coroutine
            helperObject = new GameObject("KelvinLinkHelper");
            Object.DontDestroyOnLoad(helperObject);
            helperComponent = helperObject.AddComponent<KelvinLinkHelper>();

            // IL2CPP requires casting from System.Action to UnityAction
            sceneLoadedAction = (UnityAction<Scene, LoadSceneMode>)new System.Action<Scene, LoadSceneMode>(OnSceneLoaded);
            SceneManager.sceneLoaded += sceneLoadedAction;
        }

        protected override void OnDeinitializeMod()
        {
            RLog.Msg("[KelvinLink] Mod wyłączany");

            if (sceneLoadedAction != null)
            {
                SceneManager.sceneLoaded -= sceneLoadedAction;
                sceneLoadedAction = null;
            }

            if (helperObject != null)
            {
                Object.Destroy(helperObject);
                helperObject = null;
                helperComponent = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RLog.Msg($"[KelvinLink] Załadowano scenę: {scene.name}");

            if (!string.IsNullOrEmpty(scene.name) && scene.name.ToLower().Contains("title"))
            {
                helperComponent.StartCoroutine(AddKelvinButtonCoroutine());
            }
        }

        private IEnumerator AddKelvinButtonCoroutine()
        {
            yield return new WaitForSeconds(0.15f);

            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono Canvas w menu!");
                yield break;
            }

            var existingButton = Object.FindObjectOfType<Button>();
            if (existingButton == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono przycisku do klonowania!");
                yield break;
            }

            var kelvinBtnObj = Object.Instantiate(existingButton.gameObject, existingButton.transform.parent);
            kelvinBtnObj.name = "KelvinLinkButton";

            var btn = kelvinBtnObj.GetComponent<Button>();
            var text = kelvinBtnObj.GetComponentInChildren<Text>();

            if (text != null)
                text.text = "KelvinLink";

            btn.onClick.RemoveAllListeners();
            // IL2CPP requires casting from System.Action to UnityAction
            btn.onClick.AddListener((UnityAction)new System.Action(OpenKelvinServerMenu));

            var rt = kelvinBtnObj.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition -= new Vector2(0f, 100f);
        }

        private void OpenKelvinServerMenu()
        {
            RLog.Msg("[KelvinLink] TODO: otwórz menu serwerów KelvinLink");
        }
    }

    public class KelvinLinkHelper : MonoBehaviour { }
}
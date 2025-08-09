using RedLoader;
using RedLoader.Unity.IL2CPP.Utils;
using SonsSdk;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KelvinLinkMod
{
    public class KelvinLinkClient : BasePlugin
    {
        public override string Name => "KelvinLink Client";
        public override string Author => "TwojNick";
        public override string Version => "0.1.0";

        private GameObject helperObject;
        private KelvinLinkHelper helperComponent;

        public override void OnEnable()
        {
            RLog.Msg("[KelvinLink] Mod włączony");

            // Tworzymy GameObject z MonoBehaviour, żeby mieć coroutine
            helperObject = new GameObject("KelvinLinkHelper");
            helperComponent = helperObject.AddComponent<KelvinLinkHelper>();

            // Podpinamy event ładowania sceny (jawny delegate)
            SceneManager.sceneLoaded += new UnityAction<Scene, LoadSceneMode>(OnSceneLoaded);
        }

        public override void OnDisable()
        {
            RLog.Msg("[KelvinLink] Mod wyłączony");

            // Odpinamy event
            SceneManager.sceneLoaded -= new UnityAction<Scene, LoadSceneMode>(OnSceneLoaded);

            // Usuwamy pomocniczy GameObject
            if (helperObject != null)
                GameObject.Destroy(helperObject);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RLog.Msg($"[KelvinLink] Załadowano scenę: {scene.name}");

            if (scene.name == "TitleScene")
            {
                // Odpal coroutine przez helperComponent
                helperComponent.StartCoroutine(AddKelvinButtonCoroutine());
            }
        }

        private IEnumerator AddKelvinButtonCoroutine()
        {
            // Czekamy trochę, by UI zdążyło się załadować
            yield return new WaitForSeconds(0.15f);

            var canvas = GameObject.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono Canvas w menu!");
                yield break;
            }

            var existingButton = GameObject.FindObjectOfType<Button>();
            if (existingButton == null)
            {
                RLog.Error("[KelvinLink] Nie znaleziono przycisku do klonowania!");
                yield break;
            }

            // Klonujemy istniejący przycisk i zmieniamy tekst
            var kelvinBtnObj = GameObject.Instantiate(existingButton.gameObject, existingButton.transform.parent);
            kelvinBtnObj.name = "KelvinLinkButton";

            var btn = kelvinBtnObj.GetComponent<Button>();
            var text = kelvinBtnObj.GetComponentInChildren<Text>();
            if (text != null) text.text = "KelvinLink";

            // Czyścimy poprzednie eventy i dodajemy własny listener
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                RLog.Msg("[KelvinLink] Kliknięto przycisk KelvinLink!");
                OpenKelvinServerMenu();
            });

            // Przesuwamy przycisk, by się nie nakładał
            var rt = kelvinBtnObj.GetComponent<RectTransform>();
            rt.anchoredPosition -= new Vector2(0, 100);
        }

        private void OpenKelvinServerMenu()
        {
            RLog.Msg("[KelvinLink] TODO: otwórz menu serwerów KelvinLink");
            // Tu możesz dodać wywołanie Twojego UI lub połączenie z serwerem
        }
    }

    // Prosty MonoBehaviour do uruchamiania coroutine
    public class KelvinLinkHelper : MonoBehaviour { }
}

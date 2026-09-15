using System.Collections;
using System.Linq;
using ChessBot.Bootstrap;
using ChessBot.Chess.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ChessBot.Chess.Tests
{
    public sealed class LocalChessPlayModeTests
    {
        [UnityTest]
        public IEnumerator BoardSpritesMovesFlipAndReset()
        {
            var root = new GameObject("Local chess test"); root.AddComponent<LocalChessBootstrap>();
            yield return null;
            try
            {
                var view = root.GetComponentInChildren<BoardView>(); Assert.That(view, Is.Not.Null);
                Assert.That(view.GetComponentsInChildren<Image>().Count(image => image.name == "Piece" && image.enabled), Is.EqualTo(32));
                Click(view, "Square 52"); Click(view, "Square 36"); // e2-e4, white orientation
                Assert.That(view.GetComponentsInChildren<Text>().Any(text => text.text == "Black to move"), Is.True);
                Click(view, "Flip board");
                Click(view, "Square 51"); Click(view, "Square 35"); // e7-e5, black orientation
                Assert.That(view.GetComponentsInChildren<Text>().Any(text => text.text == "White to move"), Is.True);
                Click(view, "New game");
                Assert.That(view.GetComponentsInChildren<Text>().Any(text => text.text == "No moves yet."), Is.True);
                Assert.That(view.GetComponentsInChildren<Image>().Count(image => image.name == "Piece" && image.enabled), Is.EqualTo(32));
                var tile = view.GetComponentsInChildren<Button>().Single(item => item.name == "Square 0");
                var rect = tile.GetComponent<RectTransform>();
                Canvas.ForceUpdateCanvases();
                var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
                var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits.Count, Is.GreaterThan(0)); Assert.That(hits[0].gameObject, Is.EqualTo(tile.gameObject));
                if (UnityEngine.Application.isBatchMode)
                {
                    Click(view, "Flip board");
                    string previewPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "local-chess-preview.png"));
                    ScreenCapture.CaptureScreenshot(previewPath);
                    for (int frame = 0; frame < 10; frame++) yield return null;
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
        private static void Click(BoardView view, string name)
        {
            var button = view.GetComponentsInChildren<Button>().Single(item => item.name == name);
            Assert.That(button.interactable, Is.True, name); button.onClick.Invoke();
        }
    }
}

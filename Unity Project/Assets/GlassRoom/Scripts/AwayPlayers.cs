using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GlassRoom
{
    /// <summary>
    /// People who went from the room to Among Us (amongus.html): that page checks in every 20 s
    /// in the online database (as "away_..." entries in the joins list, marked "playing"), and
    /// they still count as online here.
    /// </summary>
    public class AwayPlayers : MonoBehaviour
    {
        const string Url = "https://tytuswebsite-board-default-rtdb.firebaseio.com/joins.json?orderBy=%22%24key%22&startAt=%22away_%22&endAt=%22away_~%22";

        /// <summary>How many are playing Among Us right now.</summary>
        public static int Count { get; private set; }

        /// <summary>Their names (those who have one).</summary>
        public static readonly List<string> Names = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            var go = new GameObject("Away Players");
            DontDestroyOnLoad(go);
            go.AddComponent<AwayPlayers>();
        }

        IEnumerator Start()
        {
            while (true)
            {
                var request = UnityWebRequest.Get(Url);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success && MiniJson.Parse(request.downloadHandler.text ?? "") is Dictionary<string, object> all)
                {
                    long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    int n = 0;
                    Names.Clear();
                    foreach (var kv in all)
                        if (kv.Value is Dictionary<string, object> e && e.TryGetValue("ms", out var t)
                            && e.TryGetValue("t", out var state) && (state as string) == "playing"
                            && now - System.Convert.ToDouble(t) < 75000)
                        {
                            n++;
                            if (e.TryGetValue("n", out var who) && who is string name && name.Length > 0) Names.Add(name);
                        }
                    Count = n;
                }
                else if (request.result == UnityWebRequest.Result.Success) { Count = 0; Names.Clear(); }
                request.Dispose();
                yield return new WaitForSecondsRealtime(15f);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>Zwei sitzende Bewohner an der Haltestelle neben der Baustelle. Aufbau ohne Szenen-Aenderung.</summary>
    public class BusStopResidents : MonoBehaviour
    {
        public const string RootName = "BaustellenHaltestelle";
        public StonerNpc bruno, fiete;
        Transform _bottle, _pipe;
        ParticleSystem _smoke;
        float _nextDrink, _nextSmoke, _puffAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "City") Spawn();
        }

        public static BusStopResidents Spawn()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) return existing.GetComponent<BusStopResidents>();
            Transform site = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Baustelle" && t.parent != null && t.parent.name == "Block_04_B") { site = t; break; }
            if (site == null) return null;
            if (Resources.Load<GameObject>("Characters/NPC_Bruno") == null || Resources.Load<GameObject>("Characters/NPC_Fiete") == null)
            { Debug.LogWarning("Haltestelle: NPC-Modelle fehlen."); return null; }
            var root = new GameObject(RootName);
            // Nordkante des Grundstuecks: zwischen Bauzaun (30,6 m) und Bordstein (35 m).
            root.transform.position = new Vector3(site.position.x + 6f, 0.18f, site.position.z + 33.1f);
            var crew = root.AddComponent<BusStopResidents>();
            crew.BuildShelter();
            crew.bruno = StonerNpc.Spawn("Bruno", root.transform.TransformPoint(new Vector3(-0.85f,0,-0.18f)), Vector3.forward, root.transform);
            crew.fiete = StonerNpc.Spawn("Fiete", root.transform.TransformPoint(new Vector3(0.85f,0,-0.18f)), Vector3.forward, root.transform);
            crew.BuildProps();
            crew._nextDrink = Time.time + 5f;
            crew._nextSmoke = Time.time + 9f;
            return crew;
        }

        void BuildShelter()
        {
            Color metal = Palette.Hex("40585A"), wood = Palette.Hex("90775C"), yellow = Palette.Hex("E6C75B");
            void Box(string n, Vector3 p, Vector3 s, Color c, bool solid = false) => Shapes.Box(transform,p,s,c,0.15f,collider:solid,name:n);
            Box("BankSitz",new Vector3(0,.44f,-.18f),new Vector3(3.7f,.08f,.66f),wood,true);
            Box("BankLehne",new Vector3(0,.8f,-.57f),new Vector3(3.7f,.48f,.08f),wood,true);
            foreach (float x in new[]{-1.55f,1.55f}) Box("BankBein",new Vector3(x,.2f,-.18f),new Vector3(.1f,.4f,.5f),metal,true);
            foreach (float x in new[]{-2.1f,2.1f}) Box("Pfosten",new Vector3(x,1.25f,-.75f),new Vector3(.1f,2.5f,.1f),metal,true);
            Box("Dach",new Vector3(0,2.5f,0),new Vector3(4.6f,.14f,1.9f),metal,true);
            Box("Dachkante",new Vector3(0,2.49f,.95f),new Vector3(4.6f,.17f,.06f),yellow);
            Box("Rueckwand",new Vector3(0,1.1f,-.8f),new Vector3(4.15f,1.7f,.07f),Palette.Hex("657B7D"),true);
            Box("Fahrplan",new Vector3(1.8f,1.45f,-.748f),new Vector3(.3f,.5f,.012f),Palette.Hex("E2DBBD"));
            for (int i=0;i<6;i++) Box("FahrplanZeile",new Vector3(1.8f,1.6f-i*.06f,-.735f),new Vector3(.22f,.014f,.01f),metal);
            Box("HaltestellenMast",new Vector3(2.65f,1.25f,.3f),new Vector3(.07f,2.5f,.07f),metal,true);
            Box("HaltestellenSchild",new Vector3(2.65f,2.2f,.3f),new Vector3(.48f,.48f,.08f),yellow);
            foreach (float x in new[]{2.55f,2.75f}) Box("H",new Vector3(x,2.2f,.35f),new Vector3(.045f,.3f,.02f),metal);
            Box("HSteg",new Vector3(2.65f,2.2f,.35f),new Vector3(.2f,.045f,.02f),metal);
            Shapes.Part(PrimitiveType.Capsule,transform,new Vector3(-1.72f,.2f,.5f),new Vector3(.38f,.3f,.38f),Palette.Hex("6B5745"),.1f,euler:new Vector3(0,0,90),name:"Reisetasche");
            Box("Taschengriff",new Vector3(-1.72f,.41f,.5f),new Vector3(.18f,.025f,.03f),metal);
            Shapes.Part(PrimitiveType.Cylinder,transform,new Vector3(1.7f,.19f,.55f),new Vector3(.38f,.3f,.38f),Palette.Hex("786B7B"),.1f,euler:new Vector3(0,0,90),name:"Schlafrolle");
            foreach(float x in new[]{1.52f,1.88f}) Shapes.Part(PrimitiveType.Cylinder,transform,new Vector3(x,.19f,.55f),new Vector3(.39f,.02f,.39f),metal,0,euler:new Vector3(0,0,90),name:"Rollengurt");
        }

        void BuildProps()
        {
            _bottle = Shapes.Group(transform,"Bruno_Bierflasche");
            Color glass=Palette.Hex("65512C");
            Shapes.Part(PrimitiveType.Cylinder,_bottle,Vector3.zero,new Vector3(.065f,.09f,.065f),glass,.1f,name:"Flaschenbauch");
            Shapes.Part(PrimitiveType.Sphere,_bottle,new Vector3(0,.09f,0),new Vector3(.064f,.055f,.064f),glass,.1f,name:"Schulter");
            Shapes.Part(PrimitiveType.Cylinder,_bottle,new Vector3(0,.145f,0),new Vector3(.026f,.045f,.026f),glass,.1f,name:"Flaschenhals");
            Shapes.Part(PrimitiveType.Cylinder,_bottle,new Vector3(0,-.005f,0),new Vector3(.067f,.033f,.067f),Palette.Hex("CBB77E"),0,name:"Etikett");
            _pipe=Shapes.Group(transform,"Fiete_Pfeife");
            Shapes.Box(_pipe,new Vector3(0,0,-.005f),new Vector3(.013f,.013f,.15f),Palette.Hex("A3B9B8"),.05f,name:"Pfeifenrohr");
            Shapes.Part(PrimitiveType.Sphere,_pipe,new Vector3(0,0,.075f),new Vector3(.039f,.032f,.04f),Palette.Hex("859D9C"),.05f,name:"Pfeifenkopf");
            _smoke=StonerNpc.MakeSmoke("Atemrauch",transform,.025f,.06f,1.7f,.24f);
            UpdateProps();
        }

        void Update()
        {
            if (bruno == null || fiete == null) return;
            if (Time.time>=_nextDrink && bruno.CurrentAction==null)
            { bruno.Play("Drink"); _nextDrink=Time.time+Random.Range(20f,30f); }
            if (Time.time>=_nextSmoke && fiete.CurrentAction==null)
            { fiete.Play("Smoke"); _nextSmoke=Time.time+Random.Range(24f,34f); _puffAt=Time.time+2.8f; }
            if (_puffAt>0 && Time.time>=_puffAt)
            {
                _puffAt=-1;
                _smoke.transform.position=fiete.Head.position+Vector3.forward*.12f;
                _smoke.Emit(7);
            }
        }
        void LateUpdate() => UpdateProps();
        public void UpdateProps()
        {
            if (_bottle==null || bruno==null || fiete==null) return;
            var b=bruno.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            var f=fiete.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            var bFinger=bruno.Animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var fFinger=fiete.Animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _bottle.position=Vector3.Lerp(b.position,bFinger.position,.7f)+Vector3.forward*.025f;
            _bottle.rotation=Quaternion.FromToRotation(Vector3.up,bruno.CurrentAction=="Drink" ? (bruno.Head.position+Vector3.forward*.12f+Vector3.up*.045f-_bottle.position).normalized : Vector3.up);
            _pipe.position=Vector3.Lerp(f.position,fFinger.position,.8f)+Vector3.forward*.025f;
            _pipe.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
        }
    }
}

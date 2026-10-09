using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static partial class SprayShopAssets
    {
        static void DressInterior()
        {
            var atmosphere = shop.Find("Interior Atmosphere");
            if (atmosphere != null) { UpgradeWallBoard(atmosphere); return; }
            var main = shop;
            shop = Shapes.Group(main, "Interior Atmosphere");
            try
            {
                // Paint band and skirting frame the existing pastel walls.
                foreach (int side in new[] {-1, 1})
                {
                    Box("Dark skirting", new Vector3(side*4.78f,.17f,-4.5f),new Vector3(.025f,.22f,8.5f),Ink);
                    Box("Pink paint stripe",new Vector3(side*4.77f,1.02f,-4.5f),new Vector3(.025f,.10f,8.5f),Pink);
                }
                Box("Back skirting",new Vector3(0,.17f,-8.72f),new Vector3(9.5f,.22f,.025f),Ink);

                // Graphic posters are made from the game's own flat colours and shapes.
                Poster("NIGHT\nSESSION",-1,-2.6f,2.1f,Pink,"FRI  /  22:00");
                Poster("MAKE\nYOUR\nMARK",-1,-5.5f,2.1f,Teal,"CAN CLUB CREW");
                Poster("SKATE\n& CREATE",1,-2.7f,2.2f,Palette.Yellow,"LOCAL ART / LOCAL RIDERS");
                Poster("FRESH\nCOLOURS",1,-6.9f,2.1f,Palette.Purple,"NEW STOCK");

                // Small neon-style feature fills the gap between the can racks.
                Box("Club mural board",new Vector3(0,1.93f,-8.68f),new Vector3(2.55f,1.9f,.055f),Ink);
                for (int i=0;i<5;i++)
                    Shapes.Box(shop,new Vector3(-.8f+i*.39f,1.9f,-8.64f),new Vector3(.18f,1.6f,.035f),i%2==0?Pink:Teal,0,new Vector3(0,0,-25));
                var badge=Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(0,1.98f,-8.58f),new Vector3(1.35f,.04f,1.35f),Cream,.15f,euler:new Vector3(90,0,0),name:"Paint badge");
                Label("CC",new Vector3(0,1.99f,-8.525f),.32f,Ink);
                Label("LEAVE A MARK",new Vector3(0,1.27f,-8.53f),.09f,Cream);
                Box("Neon underline",new Vector3(0,2.97f,-8.64f),new Vector3(2.5f,.055f,.05f),Pink);
                shop.Find("Neon underline").GetComponent<Renderer>().sharedMaterial=ToonMaterials.Get(Pink,0,true,1.4f);

                // A pegboard of spare caps, with a compact price strip.
                Box("Cap pegboard",new Vector3(-4.72f,1.65f,-7.65f),new Vector3(.055f,1.2f,1.15f),Ink);
                for(int row=0;row<3;row++) for(int column=0;column<4;column++)
                {
                    Vector3 p=new Vector3(-4.65f,1.3f+row*.27f,-8.02f+column*.25f);
                    Shapes.Part(PrimitiveType.Cylinder,shop,p,new Vector3(.10f,.035f,.10f),row%2==0?Cream:Pink,.1f,euler:new Vector3(0,0,90),name:"Spare spray cap");
                }
                SideText("CAPS / TIPS",-1,new Vector3(-4.62f,2.43f,-7.65f),.11f,Ink);

                // Stock deliveries live at the sides, clear of the route to the till.
                Crate(new Vector3(3.98f,.08f,-7.12f),new Vector3(.85f,.65f,.78f));
                Crate(new Vector3(4.02f,.74f,-7.16f),new Vector3(.72f,.48f,.67f));
                Crate(new Vector3(-4.05f,.08f,-6.5f),new Vector3(.86f,.65f,.76f));
                for(int i=0;i<3;i++) Can(new Vector3(-4.3f+i*.24f,.75f,-6.45f),i+3,.75f);

                // Personal clutter makes the cashier's station feel used.
                Box("Radio",new Vector3(3.7f,1.42f,-5.4f),new Vector3(.52f,.36f,.26f),Ink);
                for(int i=0;i<2;i++)
                    Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(3.57f+i*.26f,1.42f,-5.255f),new Vector3(.16f,.012f,.16f),Cream,.1f,euler:new Vector3(90,0,0),name:"Radio speaker");
                Box("Radio antenna",new Vector3(3.8f,1.77f,-5.46f),new Vector3(.015f,.4f,.015f),Cream);
                Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(2.1f,1.35f,-5.28f),new Vector3(.12f,.11f,.12f),Cream,.1f,name:"Coffee cup");
                Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(2.1f,1.465f,-5.28f),new Vector3(.11f,.009f,.11f),Ink,0,name:"Coffee");
                Box("Receipt",new Vector3(2.6f,1.385f,-5.09f),new Vector3(.12f,.008f,.29f),Cream);
                Box("Sticker stack",new Vector3(1.48f,1.247f,-4.99f),new Vector3(.35f,.025f,.20f),Teal);
                Box("Counter sticker",new Vector3(3.66f,.57f,-4.829f),new Vector3(.41f,.21f,.012f),Palette.Yellow);
                Label("SUPPORT\nLOCAL",new Vector3(3.66f,.57f,-4.815f),.044f,Ink);

                // A board and clock on the wall tie the store to the skater community.
                UpgradeWallBoard(shop);
                Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(4.69f,2.9f,-4.5f),new Vector3(.6f,.03f,.6f),Ink,.15f,euler:new Vector3(0,0,90),name:"Clock frame");
                Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(4.645f,2.9f,-4.5f),new Vector3(.51f,.01f,.51f),Cream,0,euler:new Vector3(0,0,90),name:"Clock face");
                Box("Clock minute hand",new Vector3(4.622f,2.99f,-4.5f),new Vector3(.01f,.20f,.02f),Ink);
                Box("Clock hour hand",new Vector3(4.62f,2.9f,-4.57f),new Vector3(.01f,.025f,.14f),Ink);

                // Door mat and a plant fill the entry corners without narrowing the doorway.
                Box("Welcome mat",new Vector3(0,.069f,-1.3f),new Vector3(2.1f,.012f,.86f),Ink);
                for(int i=-3;i<=3;i++) Box("Mat stripe",new Vector3(i*.27f,.078f,-1.3f),new Vector3(.09f,.008f,.72f),Teal);
                Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(-4.18f,.33f,-1.42f),new Vector3(.53f,.27f,.53f),Pink,.2f,name:"Plant pot");
                foreach(var offset in new[]{new Vector3(0,.35f,0),new Vector3(.17f,.24f,.07f),new Vector3(-.12f,.20f,-.15f)})
                    Shapes.Part(PrimitiveType.Sphere,shop,new Vector3(-4.18f,.72f,-1.42f)+offset,new Vector3(.30f,.56f,.29f),Palette.Lime,.15f,name:"Plant leaves");

                foreach(int side in new[]{-1,1})
                {
                    Box("Pendant cable",new Vector3(side*2,3.16f,-3),new Vector3(.025f,.5f,.025f),Ink);
                    Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(side*2,2.90f,-3),new Vector3(.62f,.08f,.62f),Ink,.2f,name:"Pendant shade");
                    Shapes.Part(PrimitiveType.Cylinder,shop,new Vector3(side*2,2.81f,-3),new Vector3(.46f,.015f,.46f),Cream,0,emission:1,name:"Warm lamp diffuser");
                }
            }
            finally { shop = main; }
        }

        static void Poster(string title,int side,float z,float y,Color accent,string caption)
        {
            Box("Poster frame",new Vector3(side*4.75f,y,z),new Vector3(.06f,1.52f,1.32f),Ink);
            Box("Poster art",new Vector3(side*4.705f,y,z),new Vector3(.02f,1.40f,1.20f),accent);
            SideText(title,side,new Vector3(side*4.685f,y+.16f,z),.105f,Cream);
            SideText(caption,side,new Vector3(side*4.678f,y-.50f,z),.035f,Ink);
            foreach(float offset in new[]{-.46f,.46f})
                Box("Poster tape",new Vector3(side*4.672f,y+.68f,z+offset),new Vector3(.012f,.10f,.19f),Cream);
        }

        static void SideText(string text,int side,Vector3 pos,float size,Color color)
        {
            Label(text,pos,size,color).localRotation=Quaternion.Euler(0,side>0?90:-90,0);
        }

        static void Crate(Vector3 floor,Vector3 size)
        {
            var color=Palette.Hex("BA8868");
            Box("Delivery carton",floor+Vector3.up*size.y*.5f,size,color);
            Box("Packing tape",floor+new Vector3(0,size.y+.01f,0),new Vector3(.10f,.012f,size.z),Cream);
            Box("Carton label",floor+new Vector3(0,size.y*.5f,size.z*.5f+.01f),new Vector3(size.x*.55f,size.y*.4f,.012f),Cream);
            Label("CAN CLUB",floor+new Vector3(0,size.y*.5f,size.z*.5f+.025f),.035f,Ink);
        }
    }
}

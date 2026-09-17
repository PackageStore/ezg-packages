"""Self-test for build_plan_gen.py: synthetic manifest, asserts the op tree."""
import json, shutil, tempfile
from pathlib import Path
from build_plan_gen import generate
from pipeline_config import resolve

def selftest():
    d = tempfile.mkdtemp(prefix="bpg_test_")
    try:
        def _a(s,n,nd,r,x,y,w,h,gp,z,**kw):
            e={"screen":s,"psdName":n,"node":nd,"role":r,"x":x,"y":y,"w":w,"h":h,
               "opacity":kw.get("op",1.0),"groupPath":gp,"zOrder":z}
            if r=="art": e["asset"]=kw.get("a",n)
            if "t" in kw: e["type"]=kw["t"]
            return e
        L=[_a("t","bg","Bg","art",0,0,1080,2400,[],0,a="bg"),
           _a("t","a1","Art-One","art",100,100,200,200,["G"],1,a="a1"),
           _a("t","a2","Art-Two","art",100,350,200,100,["G"],2,a="a2",op=0.8),
           _a("t","in","Art-Inner","art",120,120,50,50,["G","Sub"],3,a="inner"),
           _a("t","hi","Text-Hi","text",400,400,100,40,[],4,t={"style":"X","content":"Hi"}),
           _a("t","sk","Skip","skip",0,0,10,10,[],5),
           _a("t","p1","Plate-A","art",0,500,300,100,[],6,a="plate"),
           _a("t","p2","Plate-B","art",0,650,400,100,[],7,a="plate"),
           _a("t","q1","Plate-C","art",0,800,250,80,[],8,a="qplate"),
           _a("t","r1","Reg-Plate","art",0,900,300,100,[],9,a="regp")]
        w=lambda n,o: Path(d,n).write_text(json.dumps(o),encoding="utf-8")
        w("psd2figma.json",{"paths":{"projectRoot":d,"psdDir":d},"frame":{"w":1080,"h":2400},
                            "figma":{"gridStyleId":"S:t,"}})
        w("psd_manifest.json",{"screens":{"t":{}},"layers":L,"collisions":[]})
        for f,v in [("components_plan.json",{"instances":{},"clusters":{
            "cx":{"registered":"Comp","stems":["regp"]}},"variantSets":[],
            "composites":[],"unpromoted":[]}),
            ("image_hashes.json",{"bg":"a","a1":"b","a2":"c","inner":"d",
            "plate":"e","qplate":"f","regp":"g"}),
            ("component_ids.json",{}),("text_styles.json",{"styles":{}}),
            ("screens.json",{"t":{"psd":"t.psd","psdW":1080,"psdH":2400,"dx":0,"dy":0,
            "walkMode":"tree"}}),("figma_extract_config.json",{"pageName":"Screens",
            "frames":{"Test":"t"},"clipLeafNames":[]}),("node_names.json",{}),
            ("nine_slice.json",{
                "plate":{"size":[200,80],"border":{"left":20,"top":15,"right":20,"bottom":15},
                    "sliceable":{"x":True,"y":True}},
                "qplate":{"size":[250,80],"border":{"left":10,"top":10,"right":10,"bottom":10},
                    "sliceable":{"x":True,"y":True},
                    "applied":{"border":[10,10,10,10],"node":"test"}},
                "regp":{"size":[200,80],"border":{"left":20,"top":15,"right":20,"bottom":15},
                    "sliceable":{"x":True,"y":True}}
            })]: w(f,v)
        c,_=resolve(["--data-dir",d,"--project-root",d]); generate(c,"t",None)
        ops=json.loads(Path(d,"build_plan_t.json").read_text())["ops"]
        bn={o["name"]:o for o in ops}; assert len(ops)==11
        assert bn["Container-G"]["parent"] is None
        assert bn["Container-Sub"]["parent"]==bn["Container-G"]["id"]
        assert bn["Art-Inner"]["parent"]==bn["Container-Sub"]["id"]
        assert bn["Bg"]["parent"] is None and bn["Bg"]["op"]=="rect"
        assert bn["Text-Hi"]["op"]=="text" and bn["Text-Hi"]["parent"] is None
        assert bn["Plate-A"]["op"]=="nineSlice" and bn["Plate-B"]["op"]=="nineSlice"
        assert bn["Plate-A"]["srcW"]==200 and bn["Plate-A"]["border"]==[20,15,20,15]
        assert bn["Plate-A"]["axes"]=={"x":True,"y":True}
        assert bn["Plate-C"]["op"]=="nineSlice", "applied single-size stem => nineSlice"
        assert bn["Plate-C"]["border"]==[10,10,10,10]
        assert bn["Reg-Plate"]["op"]=="rect", "registered component stem stays rect"
        for o in ops:
            if o["op"]=="nineSlice":
                b=o["border"]
                assert b[0]+b[2]<o["w"] and b[1]+b[3]<o["h"], f"P-8 violated on {o['name']}"
        assert all("_" not in o["name"] or o["name"].startswith("slice_") for o in ops)
        print("selftest: PASS")
    finally: shutil.rmtree(d, ignore_errors=True)

if __name__ == "__main__":
    selftest()

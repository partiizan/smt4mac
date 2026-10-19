import Foundation
import AppKit
import ScreenCaptureKit
import Vision

// Loaded inside SMT4Mac so screen-recording consent belongs to the app bundle.
private let lock = NSLock()
private var result: String? = nil
private let speech = NSSpeechSynthesizer()
private func finish(_ value: [String:Any]) {
    let data = (try? JSONSerialization.data(withJSONObject:value)) ?? Data("{}".utf8)
    lock.lock(); result = String(data:data,encoding:.utf8); lock.unlock()
}
@_cdecl("smt_result") public func smtResult() -> UnsafeMutablePointer<CChar>? {
    lock.lock(); defer {lock.unlock()}
    guard let s = result else {return nil}; result=nil; return strdup(s)
}
@_cdecl("smt_free") public func smtFree(_ p: UnsafeMutableRawPointer?) {free(p)}
private func recognize(_ img: CGImage) throws -> [[String:Any]] {
    let request=VNRecognizeTextRequest();request.recognitionLevel = .accurate;request.usesLanguageCorrection=false;request.minimumTextHeight=0.005;request.recognitionLanguages=["en-US"]
    try VNImageRequestHandler(cgImage:img,options:[:]).perform([request])
    let bitmap=NSBitmapImageRep(cgImage:img)
    let rows=(request.results ?? []).compactMap {observation -> [String:Any]? in
        guard let candidate=observation.topCandidates(1).first else{return nil}
        // Locate the name separately from OCR-recognized standing glyphs such as '=' or '+'.
        let firstLetter=candidate.string.firstIndex(where:{$0.isLetter || $0.isNumber})
        guard let start=firstLetter else {return nil}
        let name=String(candidate.string[start...])
        let box=(try? candidate.boundingBox(for:start..<candidate.string.endIndex))?.boundingBox ?? observation.boundingBox
        // Inspect the icon area left of a name. Reject a broad blue selection background as ambiguous.
        let nameX=Int(box.minX*Double(img.width))
        let rowHeight=max(8,Int(box.height*Double(img.height)))
        // Vision can merge a saturated badge into the word box. Extend color sampling
        // by badge-sized geometry, but never count white name glyphs in that extension.
        let nameEdge=nameX-2
        let left=max(0,nameX-rowHeight*2),right=min(img.width-1,max(nameEdge,Int(Double(rowHeight)*1.5)))
        let top=max(0,Int((1-box.maxY)*Double(img.height))),bottom=min(img.height-1,Int((1-box.minY)*Double(img.height)))
        var friendly=0,other=0,neutral=0
        if right>left && bottom>=top {for y in top...bottom {for x in left...right {
            if let c=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB) {
                var hue:CGFloat=0,saturation:CGFloat=0,brightness:CGFloat=0,alpha:CGFloat=0
                c.getHue(&hue,saturation:&saturation,brightness:&brightness,alpha:&alpha)
                if saturation>0.35 && brightness>0.30 {
                    if hue>=0.25 && hue<=0.88 {friendly+=1} else {other+=1}
                } else if x<=nameEdge && saturation<=0.35 && brightness>0.35 {neutral+=1}
            }
        }}}
        let samples=max(1,(right-left+1)*(bottom-top+1))
        let style: String
        if friendly>=4 && other<4 && Double(friendly)/Double(samples)<0.75 {style="Friendly"}
        else if friendly+other+neutral>=4 {style="Hostile"}
        else {style="Ignored"} // Empty badge slot: current player; blank space produces no OCR row.
        return ["name":name,"confidence":candidate.confidence,"standing":style,"badgeDebug":[left,right,top,bottom,friendly,other,neutral],"ocrBox":[box.minX,box.minY,box.width,box.height]]
    }
    return rows
}
@_cdecl("smt_request") public func smtRequest(_ raw: UnsafePointer<CChar>) {
    let text=String(cString:raw)
    Task { @MainActor in
        do {
            let args=try JSONSerialization.jsonObject(with:Data(text.utf8)) as! [String:Any]
            if args["action"] as? String == "alert" {
                speech.volume=(args["volume"] as? Float) ?? 0.7
                if args["voice"] as? Bool == true {speech.stopSpeaking();speech.startSpeaking(String((args["message"] as? String ?? "Alert").prefix(240)))}
                if args["siren"] as? Bool == true {NSSound(named:"Sosumi")?.play()}
                finish(["ok":true]);return
            }
            if args["action"] as? String == "selftest" {
                let cases:[(String,NSColor?,String)]=[("green",.green,"Friendly"),("blue",.blue,"Friendly"),("purple",.purple,"Friendly"),("red",.red,"Hostile"),("grey",.gray,"Hostile"),("neutral-equals",.white,"Hostile"),("orange",.orange,"Hostile"),("yellow",.yellow,"Hostile"),("self-no-badge",nil,"Ignored"),("dark-blue",NSColor(srgbRed:11/255.0,green:32/255.0,blue:96/255.0,alpha:1),"Friendly"),("dark-green",NSColor(srgbRed:23/255.0,green:96/255.0,blue:25/255.0,alpha:1),"Friendly")]
                var checked:[String:String]=[:]
                for (name,color,expected) in cases {
                    let image=NSImage(size:NSSize(width:500,height:100));image.lockFocus()
                    NSColor.black.setFill();NSRect(x:0,y:0,width:500,height:100).fill()
                    color?.setFill();if name == "neutral-equals" {
                        NSRect(x:28,y:43,width:12,height:2).fill();NSRect(x:28,y:49,width:12,height:2).fill()
                    } else if color != nil {NSRect(x:28,y:39,width:16,height:20).fill()}
                    ("PlayerX" as NSString).draw(at:NSPoint(x:50,y:35),withAttributes:[.font:NSFont.systemFont(ofSize:28),.foregroundColor:NSColor.white])
                    image.unlockFocus()
                    guard let cg=image.cgImage(forProposedRect:nil,context:nil,hints:nil) else {throw NSError(domain:"SMT",code:5)}
                    let rows=try recognize(cg)
                    guard let row=rows.first(where:{($0["name"] as? String)?.contains("PlayerX")==true}),row["standing"] as? String == expected else {throw NSError(domain:"SMT",code:6,userInfo:[NSLocalizedDescriptionKey:"Icon test failed: \(name) \(rows)"])}
                    checked[name]=expected
                }
                // A tall Local roster with small names must not be discarded by Vision's relative-height filter.
                let tall=NSImage(size:NSSize(width:500,height:1800));tall.lockFocus()
                NSColor.black.setFill();NSRect(x:0,y:0,width:500,height:1800).fill()
                ("PlayerX" as NSString).draw(at:NSPoint(x:50,y:1700),withAttributes:[.font:NSFont.systemFont(ofSize:14),.foregroundColor:NSColor.white])
                tall.unlockFocus()
                guard let cg=tall.cgImage(forProposedRect:nil,context:nil,hints:nil),try recognize(cg).contains(where:{($0["name"] as? String)?.contains("PlayerX")==true}) else {throw NSError(domain:"SMT",code:7,userInfo:[NSLocalizedDescriptionKey:"Small text in tall Local roster was missed"])}
                finish(["name":"PlayerX","rules":checked,"rulesPassed":true,"smallTextPassed":true]);return
            }
            guard CGPreflightScreenCaptureAccess() else {CGRequestScreenCaptureAccess();throw NSError(domain:"SMT",code:1,userInfo:[NSLocalizedDescriptionKey:"Allow SMT4Mac in System Settings → Privacy & Security → Screen Recording, then restart the app."])}
            let content=try await SCShareableContent.excludingDesktopWindows(true,onScreenWindowsOnly:true)
            if args["action"] as? String == "list" {
                finish(["windows":content.windows.filter {w in
                    let app=w.owningApplication?.applicationName.lowercased() ?? ""
                    return app.contains("eve") && w.frame.width>200 && w.frame.height>200
                }.map {w in ["id":Int(w.windowID),"title":"\(w.owningApplication?.applicationName ?? "EVE") · \(w.title ?? "Untitled")"]}]);return
            }
            guard let id=args["id"] as? Int,let window=content.windows.first(where:{Int($0.windowID)==id}) else {throw NSError(domain:"SMT",code:2,userInfo:[NSLocalizedDescriptionKey:"Selected EVE window is unavailable or minimized. Select it again."])}
            let filter=SCContentFilter(desktopIndependentWindow:window)
            let config=SCStreamConfiguration();config.width=Int(window.frame.width*2);config.height=Int(window.frame.height*2);config.showsCursor=false
            let full=try await SCScreenshotManager.captureImage(contentFilter:filter,configuration:config)
            let roi=args["roi"] as? [Double] ?? [0,0,1,1]
            guard roi.count==4,roi.allSatisfy({$0.isFinite}),roi[0]>=0,roi[1]>=0,roi[2]>0,roi[3]>0,roi[0]+roi[2]<=1.001,roi[1]+roi[3]<=1.001 else {throw NSError(domain:"SMT",code:3,userInfo:[NSLocalizedDescriptionKey:"Invalid capture area"])}
            let rect=CGRect(x:roi[0]*Double(full.width),y:roi[1]*Double(full.height),width:roi[2]*Double(full.width),height:roi[3]*Double(full.height))
            guard let img=full.cropping(to:rect) else {throw NSError(domain:"SMT",code:4,userInfo:[NSLocalizedDescriptionKey:"Capture area is empty"])}
            let rows: [[String:Any]]
            if args["scan"] as? Bool == false { rows=[] }
            else { rows=try await Task.detached(priority:.utility) {try recognize(img)}.value }
            let bitmap=NSBitmapImageRep(cgImage:img)
            var response:[String:Any] = ["rows":rows,"width":full.width,"height":full.height]
            if args["preview"] as? Bool == true {response["png"]=bitmap.representation(using:.png,properties:[:])?.base64EncodedString() ?? ""}
            finish(response)
        } catch {finish(["error":error.localizedDescription])}
    }
}

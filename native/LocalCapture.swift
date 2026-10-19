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
    let request=VNRecognizeTextRequest();request.recognitionLevel = .accurate;request.usesLanguageCorrection=false;request.recognitionLanguages=["en-US"]
    try VNImageRequestHandler(cgImage:img,options:[:]).perform([request])
    let bitmap=NSBitmapImageRep(cgImage:img)
    let rows=(request.results ?? []).compactMap {observation -> [String:Any]? in
        guard let candidate=observation.topCandidates(1).first else{return nil}
        let box=observation.boundingBox
        // Sample saturated pixels immediately left of a name. Unknown is never assumed friendly.
        let left=max(0,Int(box.minX*Double(img.width))-32),right=max(left,Int(box.minX*Double(img.width))-1)
        let top=max(0,Int((1-box.maxY)*Double(img.height))),bottom=min(img.height-1,Int((1-box.minY)*Double(img.height)))
        var red=0,blue=0,green=0
        if right>left && bottom>=top {for y in top...bottom {for x in left...right {
            if let c=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB) {
        let r=c.redComponent,g=c.greenComponent,b=c.blueComponent
        if r>0.55 && r>g*1.5 && r>b*1.3 {red+=1}
        if b>0.5 && b>g*1.2 && b>r*1.2 {blue+=1}
        if g>0.5 && g>r*1.4 && g>b*1.2 {green+=1}
            }
        }}}
        let style=red>=4 ? "Hostile" : (blue>=4 || green>=4 ? "Friendly" : "Unknown")
        return ["name":candidate.string,"confidence":candidate.confidence,"standing":style]
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
                let image=NSImage(size:NSSize(width:500,height:100));image.lockFocus()
                NSColor.black.setFill();NSRect(x:0,y:0,width:500,height:100).fill()
                ("PlayerX" as NSString).draw(at:NSPoint(x:50,y:35),withAttributes:[.font:NSFont.systemFont(ofSize:28),.foregroundColor:NSColor.white])
                image.unlockFocus()
                guard let cg=image.cgImage(forProposedRect:nil,context:nil,hints:nil) else {throw NSError(domain:"SMT",code:5)}
                finish(["rows":try recognize(cg)]);return
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

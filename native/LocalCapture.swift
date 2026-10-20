import Foundation
import AppKit
import ScreenCaptureKit
import Vision
import AVFoundation

// Loaded inside SMT4Mac so screen-recording consent belongs to the app bundle.
private let lock = NSLock()
private var result: String? = nil
private final class AlertSpeech {
    private let synth=AVSpeechSynthesizer()
    func voices() -> [AVSpeechSynthesisVoice] {
        AVSpeechSynthesisVoice.speechVoices().sorted {
            if $0.quality.rawValue != $1.quality.rawValue {return $0.quality.rawValue > $1.quality.rawValue}
            return $0.identifier < $1.identifier
        }
    }
    func resolve(_ identifier:String) -> AVSpeechSynthesisVoice? {
        let available=voices()
        if let selected=available.first(where:{$0.identifier==identifier}) {return selected}
        return available.first(where:{$0.language=="en-ZA"}) ?? available.first(where:{$0.language.hasPrefix("en-")}) ?? AVSpeechSynthesisVoice(language:"en-US") ?? available.first
    }
    func describe(_ voice:AVSpeechSynthesisVoice) -> [String:Any] {
        ["id":voice.identifier,"name":voice.name,"language":voice.language,"quality":voice.quality == .premium ? "Premium" : voice.quality == .enhanced ? "Enhanced" : "Standard"]
    }
    func utterance(_ message:String,_ volume:Float,_ identifier:String) -> AVSpeechUtterance {
        let utterance=AVSpeechUtterance(string:String(message.prefix(240)))
        utterance.voice=resolve(identifier);utterance.volume=max(0,min(1,volume));utterance.rate=AVSpeechUtteranceDefaultSpeechRate
        return utterance
    }
    func enqueue(_ message:String,_ volume:Float,_ identifier:String) {
        // AVSpeechSynthesizer queues utterances without interrupting the previous alert.
        synth.speak(utterance(message,volume,identifier))
    }
}
private let speech=AlertSpeech()
private func finish(_ value: [String:Any]) {
    let data = (try? JSONSerialization.data(withJSONObject:value)) ?? Data("{}".utf8)
    lock.lock(); result = String(data:data,encoding:.utf8); lock.unlock()
}
@_cdecl("smt_result") public func smtResult() -> UnsafeMutablePointer<CChar>? {
    lock.lock(); defer {lock.unlock()}
    guard let s = result else {return nil}; result=nil; return strdup(s)
}
@_cdecl("smt_free") public func smtFree(_ p: UnsafeMutableRawPointer?) {free(p)}
private struct TextFragment {
    let text: String
    let confidence: Float
    let box: CGRect
}
private func rosterLines(_ fragments: [TextFragment]) -> [[TextFragment]] {
    var lines: [[TextFragment]]=[]
    for fragment in fragments.sorted(by:{$0.box.midY > $1.box.midY}) {
        // Use vertical alignment, never spaces or the OCR observation count, as the row boundary.
        if let index=lines.firstIndex(where:{ line in
            let anchor=line[0].box
            return abs(anchor.midY-fragment.box.midY) <= min(anchor.height,fragment.box.height)*0.45
        }) {lines[index].append(fragment)} else {lines.append([fragment])}
    }
    return lines.map{$0.sorted(by:{$0.box.minX < $1.box.minX})}
}
// Recognize a topmost numeric count with a person-shaped grey icon. Numeric pilot
// names and numeric suffixes elsewhere in the roster remain ordinary pilot rows.
private func memberCountHeader(_ img:CGImage,_ lines:[[TextFragment]]) -> Bool {
    guard let first=lines.first,let anchor=first.first else {return false}
    let text=first.map{$0.text}.joined(separator:"").replacingOccurrences(of:",",with:"")
    guard !text.isEmpty,text.count<=6,text.allSatisfy({$0.isNumber}),lines.count>1 else {return false}
    let box=anchor.box, next=lines[1][0].box
    guard 1-box.maxY<=max(0.15,box.height*6),box.midY-next.midY>min(box.height,next.height)*1.3 else {return false}
    let bitmap=NSBitmapImageRep(cgImage:img),h=max(6,Int(box.height*Double(img.height)))
    let edge=Int(box.minX*Double(img.width))-2
    let top=max(0,Int((1-box.maxY)*Double(img.height))-h/3)
    let bottom=min(img.height-1,Int((1-box.minY)*Double(img.height))+h/3)
    guard edge>0,bottom>top else {return false}
    var rows:[Int:[Int]]=[:]
    for y in top...bottom {for x in max(0,edge-h*3)...edge {
        guard let c=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB) else {continue}
        var hue:CGFloat=0,sat:CGFloat=0,bright:CGFloat=0,alpha:CGFloat=0
        c.getHue(&hue,saturation:&sat,brightness:&bright,alpha:&alpha)
        if sat<0.25 && bright>0.40 {rows[y,default:[]].append(x)}
    }}
    let ys=rows.keys.sorted();guard let firstY=ys.first,let lastY=ys.last,lastY-firstY>=4 else {return false}
    let middle=(firstY+lastY)/2
    let upper=rows.filter{$0.key<=middle}.values.flatMap{$0},lower=rows.filter{$0.key>middle}.values.flatMap{$0}
    guard let ul=upper.min(),let ur=upper.max(),let ll=lower.min(),let lr=lower.max() else {return false}
    let head=ur-ul+1,shoulders=lr-ll+1
    return head>=2 && shoulders>=head+2 && shoulders<=h*2 && abs((ul+ur)-(ll+lr))<=h
}
private func classify(_ img: CGImage, _ fragments: [TextFragment]) -> [[String:Any]] {
    let bitmap=NSBitmapImageRep(cgImage:img)
    let lines=rosterLines(fragments)
    let skipCount=memberCountHeader(img,lines)
    return lines.enumerated().compactMap {index,line -> [String:Any]? in
        if index==0 && skipCount {return nil}
        guard let first=line.first else {return nil}
        let name=line.map{$0.text}.joined(separator:" ")
        // Only the leftmost fragment defines the badge position. Later words and numeric
        // suffixes must never sample the white letters of the preceding word as a neutral icon.
        let box=first.box
        // Inspect the icon area left of a name. Reject a broad blue selection background as ambiguous.
        let nameX=Int(box.minX*Double(img.width))
        let rowHeight=max(8,Int(box.height*Double(img.height)))
        // Vision can merge a saturated badge into the word box. Extend color sampling
        // by badge-sized geometry, but never count white name glyphs in that extension.
        let nameEdge=nameX-2
        let left=max(0,nameX-rowHeight*2),right=min(img.width-1,max(nameEdge,Int(Double(rowHeight)*1.5)))
        let top=max(0,Int((1-box.maxY)*Double(img.height))),bottom=min(img.height-1,Int((1-box.minY)*Double(img.height)))
        var friendly=0,other=0,neutral=0,visible=0
        func kind(_ x:Int,_ y:Int) -> Int {
            guard x>=0,y>=0,x<img.width,y<img.height,
                  let c=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB) else {return 0}
            var hue:CGFloat=0,saturation:CGFloat=0,brightness:CGFloat=0,alpha:CGFloat=0
            c.getHue(&hue,saturation:&saturation,brightness:&brightness,alpha:&alpha)
            if saturation>0.35 && brightness>0.30 {return hue>=0.25 && hue<=0.88 ? 1 : 2}
            return saturation<=0.35 && brightness>0.35 ? 3 : 0
        }
        // Borders and selection backgrounds continue well beyond a badge-sized row.
        // Remove their pixels before deciding standing; a few extra crop pixels are harmless.
        let reach=max(12,rowHeight*2)
        if right>left && bottom>=top {for y in top...bottom {for x in left...right {
            let k=kind(x,y)
            if k==0 || (k==3 && x>nameEdge) {continue}
            visible+=1
            let vertical=(1...reach).allSatisfy{kind(x,y-$0)==k} || (1...reach).allSatisfy{kind(x,y+$0)==k}
            let horizontal=(1...reach).allSatisfy{kind(x-$0,y)==k} || (1...reach).allSatisfy{kind(x+$0,y)==k}
            if vertical || horizontal {continue}
            if k==1 {friendly+=1} else if k==2 {other+=1} else {neutral+=1}
        }}}
        let samples=max(1,(right-left+1)*(bottom-top+1))
        let style: String
        if Double(visible)/Double(samples)>=0.75 {style="Unknown"}
        else if friendly>=4 && other<4 {style="Friendly"}
        else if other>=4 || (neutral>=4 && friendly<4) {style="Hostile"}
        else {style="Ignored"} // No visible badge: self. Blank space has no recognized row.
        return ["name":name,"confidence":line.map{$0.confidence}.min() ?? 0,"standing":style,"badgeDebug":[left,right,top,bottom,friendly,other,neutral],"ocrBox":[box.minX,box.minY,box.width,box.height]]
    }
}
private func recognize(_ img: CGImage) throws -> [[String:Any]] {
    let request=VNRecognizeTextRequest();request.recognitionLevel = .accurate;request.usesLanguageCorrection=false;request.minimumTextHeight=0.005;request.recognitionLanguages=["en-US"]
    try VNImageRequestHandler(cgImage:img,options:[:]).perform([request])
    let fragments=(request.results ?? []).compactMap {observation -> TextFragment? in
        guard let candidate=observation.topCandidates(1).first,
              let start=candidate.string.firstIndex(where:{$0.isLetter || $0.isNumber}) else {return nil}
        let box=(try? candidate.boundingBox(for:start..<candidate.string.endIndex))?.boundingBox ?? observation.boundingBox
        return TextFragment(text:String(candidate.string[start...]),confidence:candidate.confidence,box:box)
    }
    return classify(img,fragments)
}
private func localCandidate(_ img:CGImage,_ fragments:[TextFragment]) throws -> [Double] {
    let headers=fragments.filter{$0.confidence>=0.8 && $0.text.range(of:"^Local(?:\\s*\\(?[0-9]+\\)?)?$",options:[.regularExpression,.caseInsensitive]) != nil}
    var candidates:[[Double]]=[]
    for header in headers {
        let h=header.box.height
        let counts=fragments.filter{$0.confidence>=0.8 && !$0.text.isEmpty && $0.text.count<=6 && $0.text.allSatisfy({$0.isNumber}) && $0.box.minX>=header.box.minX && abs($0.box.midY-header.box.midY)<h*6}
        for count in counts {
            let rows=fragments.filter{$0.confidence>=0.75 && $0.box.maxY<count.box.minY && count.box.minY-$0.box.maxY<0.65 && abs($0.box.minX-count.box.minX)<max(h*2,0.015) && !$0.text.contains(">") && !$0.text.contains("[") && $0.text.contains(where:{$0.isLetter})}.sorted{$0.box.midY>$1.box.midY}
            guard let first=rows.first, count.box.minY-first.box.maxY<h*6 else {continue}
            var roster=[first]
            for row in rows.dropFirst(){if roster.last!.box.midY-row.box.midY>h*4 {break};roster.append(row)}
            guard roster.count>=2 else {continue}
            let left=max(0,count.box.minX-h*2.5),top=max(0,1-count.box.maxY-h*0.6)
            let right=min(1,roster.map{$0.box.maxX}.max()!+h*3)
            let bitmap=NSBitmapImageRep(cgImage:img)
            let start=Int((1-roster.last!.box.minY+h*1.5)*Double(img.height))
            var bottom=0.98
            // Prefer the first long horizontal panel separator below the pilot rows.
            if start<img.height-2 {
                for y in start..<(img.height-2) {
                    var hits=0,total=0
                    for x in stride(from:Int(left*Double(img.width)),to:Int(right*Double(img.width)),by:4){
                        guard let c=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.deviceRGB),let above=bitmap.colorAt(x:x,y:y-2)?.usingColorSpace(.deviceRGB) else {continue}
                        total+=1
                        if abs(c.redComponent-above.redComponent)>0.08 && abs(c.redComponent-c.blueComponent)<0.15 {hits+=1}
                    }
                    if total>0 && Double(hits)/Double(total)>0.8 {bottom=Double(y-2)/Double(img.height);break}
                }
            }
            guard bottom>1-roster.last!.box.minY else {continue}
            candidates.append([left,top,right-left,bottom-top])
        }
    }
    guard candidates.count==1 else {throw NSError(domain:"SMT",code:20,userInfo:[NSLocalizedDescriptionKey:candidates.isEmpty ? "Could not confidently locate an English Local header, member count and aligned pilot list." : "More than one possible Local list was found."])}
    return candidates[0]
}
private func textFragments(_ img:CGImage) throws -> [TextFragment] {
    let request=VNRecognizeTextRequest();request.recognitionLevel = .accurate;request.usesLanguageCorrection=false;request.minimumTextHeight=0.005;request.recognitionLanguages=["en-US"]
    try VNImageRequestHandler(cgImage:img,options:[:]).perform([request])
    return (request.results ?? []).compactMap {observation in
        guard let candidate=observation.topCandidates(1).first else {return nil}
        return TextFragment(text:candidate.string,confidence:candidate.confidence,box:observation.boundingBox)
    }
}
@_cdecl("smt_request") public func smtRequest(_ raw: UnsafePointer<CChar>) {
    let text=String(cString:raw)
    Task { @MainActor in
        do {
            let args=try JSONSerialization.jsonObject(with:Data(text.utf8)) as! [String:Any]
            if args["action"] as? String == "voices" {
                finish(["voices":speech.voices().map{speech.describe($0)},"automaticVoiceId":speech.resolve("")?.identifier ?? ""]);return
            }
            if args["action"] as? String == "speechcheck" {
                let utterance=speech.utterance(args["message"] as? String ?? "",(args["volume"] as? NSNumber)?.floatValue ?? 0.7,args["voiceIdentifier"] as? String ?? "")
                finish(["voiceIdentifier":utterance.voice?.identifier ?? "","volume":utterance.volume,"message":utterance.speechString]);return
            }
            if args["action"] as? String == "alert" {
                if args["voice"] as? Bool == true {speech.enqueue(String((args["message"] as? String ?? "Alert").prefix(240)),(args["volume"] as? NSNumber)?.floatValue ?? 0.7,args["voiceIdentifier"] as? String ?? "")}
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
                // Deterministically reproduce Vision splitting a spaced pilot name into words.
                // Include adjacent roster rows, a numeric suffix, an unbadged self, and 600px empty space.
                let roster=NSImage(size:NSSize(width:500,height:800));roster.lockFocus()
                NSColor.black.setFill();NSRect(x:0,y:0,width:500,height:800).fill()
                var fragments:[TextFragment]=[]
                for (index,words) in [["Gallente","Citizen","93502323"],["Gallente","Citizen","93502324"],["Logi","on","me"]].enumerated() {
                    let y=CGFloat(750-index*24)
                    if index<2 {NSColor.green.setFill();NSRect(x:6,y:y+3,width:12,height:14).fill()}
                    var x:CGFloat=26
                    for word in words {
                        let attrs:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:14),.foregroundColor:NSColor.white]
                        (word as NSString).draw(at:NSPoint(x:x,y:y),withAttributes:attrs)
                        let width=(word as NSString).size(withAttributes:attrs).width
                        fragments.append(TextFragment(text:word,confidence:1,box:CGRect(x:x/500,y:(y+3)/800,width:width/500,height:14/800)))
                        x+=width+8
                    }
                }
                roster.unlockFocus()
                guard let rosterCG=roster.cgImage(forProposedRect:nil,context:nil,hints:nil) else {throw NSError(domain:"SMT",code:8)}
                // NSImage uses point coordinates; the fixture boxes are normalized for either backing scale.
                let merged=classify(rosterCG,fragments)
                guard merged.count==3,merged[0]["name"] as? String == "Gallente Citizen 93502323",
                      merged[1]["name"] as? String == "Gallente Citizen 93502324",
                      merged[0]["standing"] as? String == "Friendly",merged[1]["standing"] as? String == "Friendly",
                      merged[2]["name"] as? String == "Logi on me",merged[2]["standing"] as? String == "Ignored"
                else {throw NSError(domain:"SMT",code:9,userInfo:[NSLocalizedDescriptionKey:"Fragmented roster regression: \(merged)"])}
                let observed=try recognize(rosterCG)
                guard observed.count==3,observed.filter({$0["standing"] as? String == "Friendly"}).count==2,
                      observed.filter({$0["standing"] as? String == "Ignored"}).count==1
                else {throw NSError(domain:"SMT",code:10,userInfo:[NSLocalizedDescriptionKey:"Vision spaced roster regression: \(observed)"])}
                // A slightly oversized crop may include a scrollbar and horizontal footer line.
                // Neither may be interpreted as a neutral badge next to OCR text.
                let borders=NSImage(size:NSSize(width:500,height:300));borders.lockFocus()
                NSColor.black.setFill();NSRect(x:0,y:0,width:500,height:300).fill()
                NSColor.white.setFill();NSRect(x:10,y:0,width:2,height:300).fill();NSRect(x:0,y:53,width:500,height:2).fill()
                ("Chong Dong" as NSString).draw(at:NSPoint(x:30,y:200),withAttributes:[.font:NSFont.systemFont(ofSize:14),.foregroundColor:NSColor.white])
                borders.unlockFocus()
                guard let borderCG=borders.cgImage(forProposedRect:nil,context:nil,hints:nil) else {throw NSError(domain:"SMT",code:11)}
                let borderRows=classify(borderCG,[TextFragment(text:"Chong Dong",confidence:1,box:CGRect(x:30/500.0,y:203/300.0,width:90/500.0,height:14/300.0)),TextFragment(text:"Footer artifact",confidence:1,box:CGRect(x:30/500.0,y:50/300.0,width:90/500.0,height:14/300.0))])
                guard borderRows.allSatisfy({$0["standing"] as? String == "Ignored"}) else {throw NSError(domain:"SMT",code:12,userInfo:[NSLocalizedDescriptionKey:"Border/empty-space regression: \(borderRows)"])}
                let blank=NSImage(size:NSSize(width:500,height:300));blank.lockFocus();NSColor.black.setFill();NSRect(x:0,y:0,width:500,height:300).fill();blank.unlockFocus()
                guard let blankCG=blank.cgImage(forProposedRect:nil,context:nil,hints:nil),try recognize(blankCG).isEmpty else {throw NSError(domain:"SMT",code:13,userInfo:[NSLocalizedDescriptionKey:"Blank crop produced roster rows"])}
                for count in ["94","104"] {
                    let image=NSImage(size:NSSize(width:400,height:150));image.lockFocus()
                    NSColor.black.setFill();NSRect(x:0,y:0,width:400,height:150).fill()
                    NSColor.lightGray.setFill();NSBezierPath(ovalIn:NSRect(x:19,y:126,width:6,height:6)).fill();NSBezierPath(ovalIn:NSRect(x:16,y:118,width:12,height:5)).fill()
                    let attrs:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:14),.foregroundColor:NSColor.white]
                    (count as NSString).draw(at:NSPoint(x:36,y:117),withAttributes:attrs)
                    var fragments=[TextFragment(text:count,confidence:1,box:CGRect(x:36/400.0,y:120/150.0,width:28/400.0,height:12/150.0))]
                    for (i,name) in ["Friendly Pilot","Citizen 93502323","12345"].enumerated(){let y=CGFloat(90-i*24);NSColor.blue.setFill();NSRect(x:17,y:y+3,width:12,height:14).fill();(name as NSString).draw(at:NSPoint(x:36,y:y),withAttributes:attrs);fragments.append(TextFragment(text:name,confidence:1,box:CGRect(x:36/400.0,y:(y+3)/150,width:140/400.0,height:14/150.0)))}
                    image.unlockFocus();guard let cg=image.cgImage(forProposedRect:nil,context:nil,hints:nil) else {throw NSError(domain:"SMT",code:14)}
                    let rows=classify(cg,fragments)
                    guard rows.count==3,rows.contains(where:{$0["name"] as? String == "12345"}),rows.allSatisfy({$0["standing"] as? String == "Friendly"}) else {throw NSError(domain:"SMT",code:15,userInfo:[NSLocalizedDescriptionKey:"Member header regression: \(rows)"])}
                    let recognized=try recognize(cg)
                    guard !recognized.contains(where:{$0["name"] as? String == count}),recognized.count==3 else {throw NSError(domain:"SMT",code:16,userInfo:[NSLocalizedDescriptionKey:"Vision member count regression: \(recognized)"])}
                }
                let detection=NSImage(size:NSSize(width:1000,height:800));detection.lockFocus();NSColor.black.setFill();NSRect(x:0,y:0,width:1000,height:800).fill();detection.unlockFocus()
                detection.lockFocus()
                let detectionAttrs:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:22),.foregroundColor:NSColor.white]
                ("Local" as NSString).draw(at:NSPoint(x:100,y:710),withAttributes:detectionAttrs)
                ("42" as NSString).draw(at:NSPoint(x:300,y:680),withAttributes:detectionAttrs)
                ("Friendly Pilot" as NSString).draw(at:NSPoint(x:300,y:640),withAttributes:detectionAttrs)
                ("Another Pilot" as NSString).draw(at:NSPoint(x:300,y:604),withAttributes:detectionAttrs)
                detection.unlockFocus()
                let detectionCG=detection.cgImage(forProposedRect:nil,context:nil,hints:nil)!
                let detectorRows=[TextFragment(text:"Local",confidence:1,box:CGRect(x:0.1,y:0.9,width:0.05,height:0.02)),TextFragment(text:"42",confidence:1,box:CGRect(x:0.3,y:0.88,width:0.03,height:0.02)),TextFragment(text:"Friendly Pilot",confidence:1,box:CGRect(x:0.3,y:0.84,width:0.15,height:0.02)),TextFragment(text:"Another Pilot",confidence:1,box:CGRect(x:0.3,y:0.80,width:0.16,height:0.02))]
                _ = try localCandidate(detectionCG,textFragments(detectionCG))
                let detected=try localCandidate(detectionCG,detectorRows)
                guard detected[0]<0.3,detected[0]+detected[2]>0.46,detected[1]<0.12,detected[1]+detected[3]>0.2 else {throw NSError(domain:"SMT",code:21)}
                guard (try? localCandidate(detectionCG,Array(detectorRows.dropFirst())))==nil else {throw NSError(domain:"SMT",code:22)}
                guard (try? localCandidate(detectionCG,detectorRows+[detectorRows[0]]))==nil else {throw NSError(domain:"SMT",code:23)}
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
            if args["action"] as? String == "detect" {
                let roi=try await Task.detached(priority:.utility){try localCandidate(full,textFragments(full))}.value
                let bitmap=NSBitmapImageRep(cgImage:full)
                finish(["roi":roi,"width":full.width,"height":full.height,"png":bitmap.representation(using:.png,properties:[:])?.base64EncodedString() ?? ""]);return
            }
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

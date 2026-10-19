import Foundation
import Darwin
let handle=dlopen(CommandLine.arguments[1],RTLD_NOW)!
typealias Request = @convention(c) (UnsafePointer<CChar>) -> Void
typealias Result = @convention(c) () -> UnsafeMutablePointer<CChar>?
typealias Free = @convention(c) (UnsafeMutableRawPointer?) -> Void
let request=unsafeBitCast(dlsym(handle,"smt_request"),to:Request.self)
let result=unsafeBitCast(dlsym(handle,"smt_result"),to:Result.self)
let freeResult=unsafeBitCast(dlsym(handle,"smt_free"),to:Free.self)
func call(_ args:[String:Any]) -> [String:Any] {
    let data=try! JSONSerialization.data(withJSONObject:args)
    String(data:data,encoding:.utf8)!.withCString{request($0)}
    let deadline=Date().addingTimeInterval(30)
    while Date()<deadline {
        RunLoop.main.run(until:Date().addingTimeInterval(0.1))
        if let p=result(){let text=String(cString:p);freeResult(p);let response=try! JSONSerialization.jsonObject(with:Data(text.utf8)) as! [String:Any];precondition(response["error"]==nil,text);return response}
    }
    fatalError("Speech bridge timed out")
}
let catalog=call(["action":"voices"])
let voices=catalog["voices"] as! [[String:Any]]
precondition(!voices.isEmpty,"No voices available on macOS runner")
let automatic=catalog["automaticVoiceId"] as! String
precondition(voices.contains{($0["id"] as? String)==automatic})
let za=voices.filter{($0["language"] as? String)=="en-ZA"}
if let preferred=za.first {precondition(automatic==preferred["id"] as! String)}
for identifier in [voices.last!["id"] as! String,"","missing.voice.fixture"] {
    let utterance=call(["action":"speechcheck","voiceIdentifier":identifier,"volume":0.42,"message":"Alert, hostiles entering local"])
    precondition(utterance["voiceIdentifier"] as! String == (identifier.isEmpty || identifier=="missing.voice.fixture" ? automatic : identifier))
    precondition(abs((utterance["volume"] as! Double)-0.42)<0.001)
    precondition(utterance["message"] as! String=="Alert, hostiles entering local")
}
let bounded=call(["action":"speechcheck","volume":2,"message":String(repeating:"x",count:300)])
precondition((bounded["message"] as! String).count==240 && (bounded["volume"] as! Double)==1)
print("PASS: installed voices, South African preference, explicit and missing voice fallback, shared volume and utterance limits (no screen permission required)")

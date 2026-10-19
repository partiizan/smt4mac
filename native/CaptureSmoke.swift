import Foundation
import Darwin
let handle=dlopen(CommandLine.arguments[1],RTLD_NOW)!
typealias Request = @convention(c) (UnsafePointer<CChar>) -> Void
typealias Result = @convention(c) () -> UnsafeMutablePointer<CChar>?
typealias Free = @convention(c) (UnsafeMutableRawPointer?) -> Void
let request=unsafeBitCast(dlsym(handle,"smt_request"),to:Request.self)
let result=unsafeBitCast(dlsym(handle,"smt_result"),to:Result.self)
let freeResult=unsafeBitCast(dlsym(handle,"smt_free"),to:Free.self)
"{\"action\":\"selftest\"}".withCString {request($0)}
let deadline=Date().addingTimeInterval(30)
while Date()<deadline {
    RunLoop.main.run(until:Date().addingTimeInterval(0.1))
    if let p=result(){let text=String(cString:p);freeResult(p)
        guard text.contains("PlayerX"), !text.contains("error") else {fatalError("OCR self-test failed: \(text)")}
        print("PASS: production native bridge and Vision OCR recognize PlayerX without screen permissions")
        exit(0)
    }
}
fatalError("Native bridge did not respond")

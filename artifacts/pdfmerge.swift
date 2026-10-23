import Foundation
import PDFKit

let args = CommandLine.arguments
guard args.count >= 3 else {
    FileHandle.standardError.write("usage: pdfmerge out.pdf in1.pdf ...\n".data(using: .utf8)!)
    exit(2)
}
let out = PDFDocument()
for path in args.dropFirst(2) {
    if let doc = PDFDocument(url: URL(fileURLWithPath: path)) {
        for i in 0..<doc.pageCount {
            if let page = doc.page(at: i) { out.insert(page, at: out.pageCount) }
        }
    }
}
if !out.write(to: URL(fileURLWithPath: args[1])) { exit(1) }

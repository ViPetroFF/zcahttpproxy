FilePath = "channel.xml"
Const ForReading = 1, ForWriting = 2, ForAppending = 8
Set oFSO = CreateObject("Scripting.FileSystemObject")
Set fFrom = oFSO.OpenTextFile(FilePath, ForReading, False)
newFile = "new.xml"
Set fTo = oFSO.OpenTextFile(newFile, ForWriting, True)

index = 1

Do While fFrom.AtEndOfStream <> True
	retline = fFrom.ReadLine
	newline = retline
	If "    <channel" = Left(retline, 12) Then
		ndx = InStr(retline, " id=""")
		If ndx > 0 Then
			ndxx = InStr(ndx + 5, retline, """")
			If ndxx > 0 Then newline = Left(retline, ndx + 4) & index & Mid(retline, ndxx)
		End If
	
		ndx = InStr(newline, " spc=""")
		If ndx > 0 Then
			ndxx = InStr(ndx + 6, newline, """")
			If ndxx > 0 Then newline = Left(newline, ndx + 5) & index & Mid(newline, ndxx)
		End If

		index = index + 1
	End If
	fTo.WriteLine(newline)
Loop

fFrom.Close
fTo.Close

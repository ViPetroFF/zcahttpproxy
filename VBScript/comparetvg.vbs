rem SetLocale "ru"
Const ForReading = 1, ForWriting = 2, ForAppending = 8

Set oFSO = CreateObject("Scripting.FileSystemObject")
rem Set fLog = oFSO.OpenTextFile("log.txt", ForWriting, True)
rem MsgBox
Set NameToIDDictionary = CreateObject("Scripting.Dictionary")

LoadXmlTV NameToIDDictionary

Const FilePath = "TVGMapNames.ini"
Const newFile = "notfound.ini"

Set fFrom = oFSO.OpenTextFile(FilePath, ForReading, False)
Set fTo = oFSO.OpenTextFile(newFile, ForWriting, True)

Do While fFrom.AtEndOfStream <> True
	displayName = ""
	tvid=""
	inline = fFrom.ReadLine
	If Not "#" = Left(inline, 1) Then
		ndx = InStr(inline, "=")
		If ndx > 0 Then
			ndxx = InStr(ndx + 1, inline, ",")
			If ndxx > 0 Then
				displayName = Mid(inline, ndx + 1, ndxx - ndx - 1)
				displayName = LCase(displayName)
				tvid = Mid(inline, ndxx + 1)
			End If
		End If

		If NameToIDDictionary.Exists(displayName) Then
			tvidDic = NameToIDDictionary.Item(displayName)
			rem outline = Left(inline, ndx) & NameToIDDictionary.Item(displayName)
			outline = Left(inline, ndxx) & tvidDic
			rem fLog.WriteLine(outline & "==" & tvid & "--" & displayName)
			If Not tvid = tvidDic Then
				fTo.WriteLine(outline)
			End If
		Else
			fTo.WriteLine(Left(inline, ndxx))
		End If
	Else
		fTo.WriteLine(inline)
	End If
Loop

fFrom.Close
fTo.Close
rem fLog.Close

Sub LoadXmlTV(dictionary)
	Const xmlTVFilePath = "xmltv.xml"
	Set fXml = oFSO.OpenTextFile(xmlTVFilePath, ForReading, False)

	On Error Resume Next

	Do While fXml.AtEndOfStream <> True
		tvid=""
		retline = fXml.ReadLine

		If "<channel" = Left(retline, 8) Then
			ndx = InStr(retline, " id=""")
			If ndx > 0 Then
				ndxx = InStr(ndx + 6, retline, """")
				If ndxx > 0 Then tvid = Mid(retline, ndx + 5, ndxx - ndx - 5)
				If Len(tvid) > 0 Then
					rem fXml.ReadLine
					nextline = fXml.ReadLine
				End If
			End If

			displayName = "none"
			ndx = InStr(nextline, "<display-name lang=""ru"">")
			If ndx > 0 Then
				ndxx = InStr(ndx + 24, nextline, "</display-name>")
				If ndxx > 0 Then displayName = Mid(nextline, ndx + 24, ndxx - ndx - 24)
			End If
		ElseIf "<programme" = Left(retline, 10) Then exit do
		End If

		If Len(tvid) > 0 Then
			displayName = DecodeUTF8(displayName)
			displayName = Replace(displayName," ","_")
			displayName = Replace(displayName,"+","_")
			displayName = Replace(displayName,"/","_")
			displayName = Replace(displayName,".","_")
			displayName = LCase(displayName)
			dictionary.Add displayName, tvid
			rem fLog.WriteLine(displayName & "==" & tvid)
		End If
	Loop

	On Error Goto 0
	fXml.Close
End Sub

Function DecodeUTF8(s)
	Const mult6bit = &h40
	Const DefChar = "?"
    Dim i, c, n, b, d, e

    i = 1
    Do While i <= len(s)
        c = asc(mid(s,i,1))
		n = 0 : d = 0
        If (c and &hE0) = &hC0 Then
			n=1
        Elseif (c and &hF0) = &hE0 Then
			n=2
        Elseif (c and &hF8) = &hF0 Then
			n=3
        Elseif (c and &hFC) = &hF8 Then
			n=4
        Elseif (c and &h80) > 0 Then
            ' Not supported byte
			c = asc(DefChar)
        End If

		If n > 0 and i+n <= len(s) Then
			For it = n to 0 Step -1				
				If it > 0 Then
					e = asc(mid(s,i+it,1))
					If (e and &hC0) <> &h80 Then
						' Unexpected next byte
						d = asc(DefChar)
						Exit For
					Else
						b = e and &h3F
					End If
				Else
					b = c and &h1F
				End If
				d = d + b * mult6bit^(n-it)
			Next
		ElseIf n = 0 Then
			d = c
		Else
			' Unexpected end of string
			d = asc(DefChar)
		End If

		s = left(s,i-1) + chrw(d) + mid(s,i+n+1)
        i = i + 1
    Loop

    DecodeUTF8 = s 
End Function
;| =============================================================================================================================================================================================

Multiple-PDF-Import.lsp

This LISP program allows you to import multiple pages from a PDF file into your AutoCAD drawing. You can specify the starting point, page range, 
and layout spacing, and the program will automatically place each page in sequence. It's a quick and easy way to manage PDF content in your designs.

Test Environment: AutoCAD Electrical 2024
Author: Arshdeep Singh

Version 1.0 - January 20, 2025
Initial Release

==============================================================================================================================================================================================|;
  
;START OF THE PROGRAM

;=============================================================================================================================================================================================
;================================================================ Insert-pdf-on-existing-drawing Function ====================================================================================
;=============================================================================================================================================================================================

; Loops PDFIMPORT command and inserts the specified pages on the existing drawing according to user settings
; Function Syntax: (Insert-pdf-on-existing-drawing File StartPage EndPage Scale Ang PageHeight PageWidth Rows HorOffset VerOffset InsPt)
; Function Returns: Nothing

(defun Insert-pdf-on-existing-drawing (File StartPage EndPage Scale Ang PageHeight PageWidth Rows HorOffset VerOffset InsPt / CurrentPage Index NextInsPt ImportFailed ErrorObject)

  (setq CurrentPage StartPage)                                                                                                ; Initialize current page as start page
  (setq Index 1)                                                                                                              ; Initialize index
  (setq NextInsPt InsPt)                                                                                                      ; Initialize Next Insertion point as the Current Insertion point
  (setq ImportFailed nil)                                                                                                     ; Initialize Import Failed Flag
  
  (while (<= CurrentPage EndPage)                                                                                             ; While current page is less than total pages

    (setq ErrorObject (vl-catch-all-apply '(lambda ()                                                                         ; Attempt to execute the PDF Import command, capturing any errors
      (command "_-pdfimport" "_Fi" File (itoa CurrentPage) NextInSPt Scale Ang)))                                             ; PDF Import command with parameters
    )

    (if (= (getvar "ERRNO") 0)                                                                                                ; Check if the command completed successfully
      (progn                                                                                                                  ; If the command failed, then do the following
        (princ (strcat "\nThe specified page number " (itoa CurrentPage) " does not exist in the PDF. "))                     ; Print error message for missing page
        (princ "Aborting the PDF Import command.")                                                                            ; Notify that the command is being aborted
        (princ (strcat "\nMulti PDF Import Command Aborted. Total Pages Imported: " (itoa (1- CurrentPage))))                 ; Print import summary message
        (setq CurrentPage EndPage)                                                                                            ; Set CurrentPage to EndPage to stop further processing
        (setq ImportFailed T)                                                                                                 ; Set a flag Import Failed to stop downstream processes
      )
    )

    (if (= Index Rows)                                                                                                        ; Check if Index is same as the max number drawings in a row
      (progn                                                                                                                  ; If true, then do the following
        (setq NextInsPt (list (car InsPt) (- (cadr InsPt) VerOffset (* PageHeight Scale)) (caddr InsPt)))                     ; Calculate the next insertion point below (Y Offset)
        (setq Index 1)                                                                                                        ; Reset index back to 1
        (setq InsPt NextInsPt)                                                                                                ; Update Insertion point definition to used with next row offset
      )                                                                                                                       ; End if true
      (progn                                                                                                                  ; If false, then do the following
        (setq NextInsPt (list (+ (car NextInsPt) HorOffset (* PageWidth Scale)) (cadr NextInsPt) (caddr NextInsPt)))          ; Calculate the next insertion point towards right (X Offset)
        (setq Index (1+ Index))                                                                                               ; Increment the index by 1
      )                                                                                                                       ; End if false
    )                                                                                                                         ; End if    
    (setq CurrentPage (1+ CurrentPage))                                                                                       ; Increment the current page number by 1
  )                                                                                                                           ; End of while loop
  
  (command)                                                                                                                   ; Cancel command
  (command "zoom" "extents")                                                                                                  ; Zoom extents on the drawing
  
  (if (not ImportFailed)                                                                                                      ; If all imports were successful
    (princ (strcat "\nMulti PDF Import Command Complete. Total Pages Imported: " (itoa (1- CurrentPage))))                    ; Then print import summary message
  )
)


;=============================================================================================================================================================================================
;=================================================================== Insert-pdf-as-new-drawing Function ======================================================================================
;=============================================================================================================================================================================================

; Loops through the PDFIMPORT command to create a new drawing for each specified page of the PDF and inserts the corresponding page into the new drawing
; Function Syntax: (Sheet-Renum-Eval ListName)
; Function Returns: Nothing

(defun Insert-pdf-as-new-drawing (File StartPage EndPage Scale Ang InsPt FilePath / CurrentPage PDFFileName ImportFailed Dcl-id Import ErrorObject FileName)

  (setq CurrentPage StartPage)                                                                                                ; Initialize current page as start page
  (setq PDFFileName (vl-filename-base File))                                                                                  ; Extract the filename without the extension
  (setq ImportFailed nil)                                                                                                     ; Initialize Import Failed Flag
  
  (setq Dcl-id (load_dialog (strcat "MultiplePDFImport.dcl")))                                                                ; Load the Warning Dialog Box
  (if (not (new_dialog "NewDrawingWarning" Dcl-id)) (exit))                                                                   ; Exit the code if unable to locate the Dialog Box File  
  (action_tile "Continue" "(setq Import T) (done_dialog)" )                                                                   ; Continue button pressed, Set Import to true
  (start_dialog)                                                                                                              ; Start the dialog box
  (unload_dialog Dcl-id)                                                                                                      ; Unload dialog box id

  (if Import                                                                                                                  ; If Import is selected
    (progn                                                                                                                    ; Do the following
      (while (<= CurrentPage EndPage)                                                                                         ; While current page is less than total pages
        (command "erase" "all" "")                                                                                            ; Erase all objects
        
        (setq ErrorObject (vl-catch-all-apply '(lambda ()                                                                     ; Attempt to execute the PDF Import command, capturing any errors
          (command "_-pdfimport" "_Fi" File (itoa CurrentPage) '(0 0 0) Scale Ang)))                                          ; PDF Import command with parameters
        )

        (if (= (getvar "ERRNO") 0)                                                                                            ; Check if the command completed successfully
          (progn                                                                                                              ; If the command failed, then do the following
            (princ (strcat "\nThe specified page number " (itoa CurrentPage) " does not exist in the PDF. "))                 ; Print error message for missing page
            (princ "Aborting the PDF Import command.")                                                                        ; Notify that the command is being aborted
            (princ (strcat "\nMulti PDF Import Command Aborted. Total Pages Imported: " (itoa (1- CurrentPage))))             ; Print import summary message
            (setq CurrentPage EndPage)                                                                                        ; Set CurrentPage to EndPage to stop further processing
            (setq ImportFailed T)                                                                                             ; Set a flag Import Failed to stop downstream processes
          )
        )

        (if (not ImportFailed)                                                                                                ; If the last import was successful
          (progn                                                                                                              ; Do the following
            (if (not (equal (substr FilePath (strlen FilePath)) "\\")) (setq FilePath (strcat FilePath "\\")))                ; Add \\ to the end of the folder path if they are missing
            (setq FileName (strcat FilePath PDFFileName "-Page " (itoa CurrentPage)".dwg"))                                   ; Dynamically generate a dwg file name
            (if (findfile Filename)                                                                                           ; Check if another file with same name exits in the destination folder
              (command "_.SAVEAS" "2018" (strcat FilePath PDFFileName "-Page " (itoa CurrentPage)".dwg") "Y")                 ; If found, Save as and overwrite the dwg file
              (command "_.SAVEAS" "2018" (strcat FilePath PDFFileName "-Page " (itoa CurrentPage)".dwg"))                     ; Else save the drawing as a new dwg file
            )
          )
        )
        
        (setq CurrentPage (1+ CurrentPage))                                                                                   ; Increment the current page number by 1
      )                                                                                                                       ; End of while loop       

      (command)                                                                                                               ; Cancel command
      (command "zoom" "extents")                                                                                              ; Zoom extents on the drawing
      
      (if (not ImportFailed)                                                                                                  ; If all imports were successful
        (princ (strcat "\nMulti PDF Import Command Complete. Total Pages Imported: " (itoa (1- CurrentPage))))                ; Then print import summary message
        (command "._UNDO" "2")                                                                                                ; Else undo last two steps to restore the last drawing
      )
    )
    (princ "\nPDF import cancelled.")                                                                                         ; Print import cancelled message if Cancel button was pressed to the warning
  )
)


;=============================================================================================================================================================================================
;==================================================================== Update-Paper-Size-List-Box Function ====================================================================================
;=============================================================================================================================================================================================

; This function updated the default option available in the paper size drop down list
; Function Syntax: (Update-Paper-Size-List-Box "Imperial")
; Function Returns: Nothing

(defun Update-PaperSize-ListBox (ListType / ImperialPaperSizesList MetricPaperSizesList PaperSizesList) 
  
  (setq ImperialPaperSizeNames '("ANSI-A  8.50 x 11.00 Inches" "ANSI-B  11.00 x 17.00 Inches" "ANSI-C  17.00 x 22.00 Inches" "ANSI-D  22.00 x 34.00 Inches" "ANSI-E  34.00 x 44.00 Inches" "ARCH-A  9.00 x 12.00 Inches" "ARCH-B  12.00 x 18.00 Inches" "ARCH-C  18.00 x 24.00 Inches" "ARCH-D  24.00 x 36.00 Inches" "ARCH-E  36.00 x 48.00 Inches"))
  (setq MetricPaperSizesList '("A4  210.00 x 297.00 mm" "A3  297.00 x 420.00 mm" "A2  420.00 x 594.00 mm" "A1  594.00 x 841.00 mm" "A0  841.00 x 1189.00 mm" "B5  176.00 x 250.00 mm" "B4  250.00 x 353.00 mm" "B3  353.00 x 500.00 mm" "B2  500.00 x 707.00 mm" "B1  707.00 x 1000.00 mm" "B0  1000.00 x 1414.00 mm"))
    
  (if (= ListType "Imperial") (Setq PaperSizesList ImperialPaperSizeNames))                                                   ; If imperial toggle is selected, use imperial List
  (if (= ListType "Metric") (Setq PaperSizesList MetricPaperSizesList))                                                       ; If metric toggle is selected, use metric list
   
  (start_list "PaperSize")                                                                                                    ; Start List command
  (mapcar 'add_list PaperSizesList)                                                                                           ; Add items to List Box from the specified list
  (end_list)                                                                                                                  ; End List command
  (set_tile "PaperSize" "0")                                                                                                  ; Set the first item in the list as the default

)


;=============================================================================================================================================================================================
;=================================================================== Update-Paper-Size-EditBox Function ======================================================================================
;=============================================================================================================================================================================================

; This function populates and updates the page height and page width edit boxes
; Function Syntax: (Update-Paper-Size-EditBox)
; Function Returns: Nothing

(defun Update-PaperSize-EditBox ( / ImperialPaperSizes MetricPaperSizes Index PaperSize) 
  
  (setq ImperialPaperSizes '(("8.50" "11.00") ("11.00" "17.00") ("17.00" "22.00") ("22.00" "34.00") ("34.00" "44.00") ("9.00" "12.00") ("12.00" "18.00") ("18.00" "24.00") ("24.00" "36.00") ("36.00" "48.00")))
  (setq MetricPaperSizes '(("210.00" "297.00") ("297.00" "420.00") ("420.00" "594.00") ("594.00" "841.00") ("841.00" "1189.00") ("176.00" "250.00") ("250.00" "353.00") ("353.00" "500.00") ("500.00" "707.00") ("707.00" "1000.00") ("1000.00" "1414.00")))

  (setq Index (get_tile "PaperSize"))                                                                                             ; Get the index number of selected item from Paper Size drop down
  (setq Index (atoi Index))                                                                                                       ; Convert index from string to integer
  
  (if (= (get_tile "Imperial") "1") (setq PaperSize (nth Index ImperialPaperSizes)))                                              ; If Imperial option is selected, set Paper Size list to Imperial Paper Sizes
  (if (= (get_tile "Metric") "1") (setq PaperSize (nth Index MetricPaperSizes)))                                                  ; If Metric option is selected, set Paper Size list to Metric Paper Sizes

  (set_tile "PageHeight" (nth 0 PaperSize))                                                                                       ; Populate page height edit box based on the selected index
  (set_tile "PageWidth" (nth 1 PaperSize))                                                                                        ; Populate page width edit box based on the selected index

)


;=============================================================================================================================================================================================
;====================================================================== Update-Option-Visibility Function ====================================================================================
;=============================================================================================================================================================================================

; This function enables and disables dialog box controls based on which option is selected
; Function Syntax: (Update-Option-Visibility "Option-A")
; Function Returns: Nothing

(defun Update-Option-Visibility (Option / ) 

  (if (= Option "Option-A")                                                                                                       ; If Option-A is selected
    (progn                                                                                                                        ; Then do the following
      (mode_tile "PaperSize" 0)                                                                                                   ; Enable Paper Size Dropdown
      (mode_tile "Metric" 0)                                                                                                      ; Enable Metric option
      (mode_tile "Imperial" 0)                                                                                                    ; Enable Imperial option
      (mode_tile "PageHeight" 0)                                                                                                  ; Enable Paper Height Edit Box
      (mode_tile "PageWidth" 0)                                                                                                   ; Enable Paper Width Edit Box
      (mode_tile "Rows" 0)                                                                                                        ; Enable Maximum Rows Edit Box
      (mode_tile "HorizontalOffset" 0)                                                                                            ; Enable Horizontal offset edit box
      (mode_tile "VerticalOffset" 0)                                                                                              ; Enable Vertical offset edit box
      (mode_tile "FilePath" 1)                                                                                                    ; Disable filepath edit box
      (mode_tile "Browse" 1)                                                                                                      ; Disable browse button
    ) 
  )
  
  (if (= Option "Option-B")                                                                                                       ; If Option-B is selected
    (progn                                                                                                                        ; Then do the following
      (mode_tile "PaperSize" 1)                                                                                                   ; Disable Paper Size Dropdown
      (mode_tile "Metric" 1)                                                                                                      ; Disable Metric option
      (mode_tile "Imperial" 1)                                                                                                    ; Disable Imperial option
      (mode_tile "PageHeight" 1)                                                                                                  ; Disable Paper Height Edit Box
      (mode_tile "PageWidth" 1)                                                                                                   ; Disable Paper Width Edit Box
      (mode_tile "Rows" 1)                                                                                                        ; Disable Maximum Rows Edit Box
      (mode_tile "HorizontalOffset" 1)                                                                                            ; Disable Horizontal offset edit box
      (mode_tile "VerticalOffset" 1)                                                                                              ; Disable Vertical offset edit box
      (mode_tile "FilePath" 0)                                                                                                    ; Enable filepath edit box
      (mode_tile "Browse" 0)                                                                                                      ; Enable browse button
    ) 
  )
  
)


;=============================================================================================================================================================================================
;================================================================ Insert-pdf-on-existing-drawing Function ====================================================================================
;=============================================================================================================================================================================================

; This function is written by Lee Mac and it utilizes the BrowseForFolder method of the Windows Shell Object to provide a dialog interface through which the user may select a directory. 
; Function Syntax: (Browse for Folder)
; Function Returns: FolderPath
 
(defun BrowseForFolder ( / err fld FolderPath shl slf )
  (setq err (vl-catch-all-apply
    (function (lambda ( / app hwd )
      (if (setq app (vlax-get-acad-object)
        shl (vla-getinterfaceobject app "shell.application")
        hwd (vl-catch-all-apply 'vla-get-hwnd (list app))
        fld (vlax-invoke-method shl 'browseforfolder (if (vl-catch-all-error-p hwd) 0 hwd) "" 0 ""))
        (setq slf (vlax-get-property fld 'self)
        FolderPath (vlax-get-property slf 'path)
        FolderPath (vl-string-right-trim "\\" (vl-string-translate "/" "\\" FolderPath)))
      ))
    ))
  )
  
  (if slf (vlax-release-object slf))
  (if fld (vlax-release-object fld))
  (if shl (vlax-release-object shl))
  (if (vl-catch-all-error-p err) (prompt (vl-catch-all-error-message err)) FolderPath)

)


;=============================================================================================================================================================================================
;=================================================================== Check-User-Input-Limits Function ========================================================================================
;=============================================================================================================================================================================================

; This function checks that the numbers entered by the user in the dialog box are within permissible limits. 
; Function Syntax: (Check-User-Input-Limits)
; Function Returns: Nothing

(defun Check-User-Input-Limits ( / )

  (if (or (not (numberp (read (get_tile "StartPage")))) (< (atoi (get_tile "StartPage")) 1)) (set_tile "StartPage" "1"))                                              ; Force start page to minimum of 1
  (if (or (not (numberp (read (get_tile "EndPage")))) (< (atoi (get_tile "EndPage")) (atoi (get_tile "StartPage")))) (set_tile "EndPage" (get_tile "StartPage")))     ; Force end page page to be not less than start page  
  (if (or (not (numberp (read (get_tile "Scale")))) (<= (atoi (get_tile "Scale")) 0)) (set_tile "Scale" "1"))                                                         ; Force scale to be more than 0
  (if (or (not (numberp (read (get_tile "PageHeight")))) (<= (atoi (get_tile "PageHeight")) 0)) (set_tile "PageHeight" "1"))                                          ; Force PageHeight to be more than 0
  (if (or (not (numberp (read (get_tile "PageWidth")))) (<= (atoi (get_tile "PageWidth")) 0)) (set_tile "PageWidth" "1"))                                             ; Force PageWidth to be more than 0
  (if (or (not (numberp (read (get_tile "Rows")))) (<= (atoi (get_tile "Rows")) 0)) (set_tile "Rows" "1"))                                                            ; Force Rows to be more than 0
  (if (not (numberp (read (get_tile "Angle")))) (set_tile "Angle" "0"))                                                                                               ; Force Angle to be a number
  (if (not (numberp (read (get_tile "HorizontalOffset")))) (set_tile "HorizontalOffset" "0"))                                                                         ; Force Horizontal offset to be a number
  (if (not (numberp (read (get_tile "VerticalOffset")))) (set_tile "VerticalOffset" "0"))                                                                             ; Force Vertical offset to be a number

)


;=============================================================================================================================================================================================
;======================================================================= MPDFIMPORT - Main Command ===========================================================================================
;=============================================================================================================================================================================================

(defun c:mpdfimport ( / CmdEchoLastVal DesktopPath File DialogBoxName Dcl-id Import Existing New StartPage EndPage InsPtReq Scale Ang PageHeight PageWidth Rows HorOffset VerOffset FilePath InsPt)

  (defun *error* (x) (setvar "cmdecho" CmdEchoLastVal) (princ "\n*Cancel*"))                                                      ; Define error function
  
  (setq CmdEchoLastVal (getvar "cmdecho"))                                                                                        ; Get cmdecho variable's current value
  (setvar "cmdecho" 0)                                                                                                            ; Set cmdecho variable to 0
  (setq DesktopPath (strcat (getenv "USERPROFILE") "\\Desktop\\"))                                                                ; Get current user'd desktop path
  (setq File (getfiled "Select PDF file to import" "" "pdf" 8))                                                                   ; Select a PDF file to import
  
  (if File                                                                                                                        ; If a valid PDF file is selected
   (progn
      (setq DialogBoxName "MultiplePDFImport")                                                                                    ; Define Dialog Box Name, (.dcl) file must be present within AutoCAD search path
      (setq Dcl-id (load_dialog (strcat DialogBoxName ".dcl")))                                                                   ; Load the Dialog Box
      (if (not (new_dialog DialogBoxName Dcl-id)) (exit))                                                                         ; Exit the code if unable to locate the Dialog Box File  
      
      (Update-Option-Visibility "Option-A")                                                                                       ; Select Option A - Place on existing drawing by default
      (Update-PaperSize-ListBox "Imperial")                                                                                       ; Select Imperial Paper Sizes by default
      (Update-PaperSize-EditBox)                                                                                                  ; Populate Paper Size Dimensions

      (set_tile "FilePath" DesktopPath)                                                                                           ; Populate the FilePath as Desktop Path by Default

      (action_tile "Metric" "(Update-PaperSize-ListBox \"Metric\") (Update-PaperSize-EditBox) (Check-User-Input-Limits)")         ; Metric toggle pressed, Update paper size list and paper width and height edit box
      (action_tile "Imperial" "(Update-PaperSize-ListBox \"Imperial\") (Update-PaperSize-EditBox) (Check-User-Input-Limits)")     ; Imperial toggle pressed, Update paper size list and paper width and height edit box
      (action_tile "PaperSize" "(Update-PaperSize-EditBox) (Check-User-Input-Limits)")                                            ; New Paper size selected from list, Update paper width and height edit box
      (action_tile "Existing" "(Update-Option-Visibility \"Option-A\") (Check-User-Input-Limits)")                                ; Option A selected, Disable Option B settings
      (action_tile "New" "(Update-Option-Visibility \"Option-B\") (Check-User-Input-Limits)")                                     ; Option B selected, Disable Option A settings
      (action_tile "Browse" "(progn (setq FilePath (BrowseForFolder)) (if FilePath (set_tile \"FilePath\" FilePath)))")           ; Browse button pressed, Prompt user for folder location    
      (action_tile "StartPage" "(Check-User-Input-Limits)")                                                                       ; Start page number changed, check limits
      (action_tile "EndPage" "(Check-User-Input-Limits)")                                                                         ; End page number changed, check limits
      (action_tile "Rows" "(Check-User-Input-Limits)")                                                                            ; Rows changed, check limits
      (action_tile "PageHeight" "(Check-User-Input-Limits)")                                                                      ; Page Height changed, check limits
      (action_tile "PageWidth" "(Check-User-Input-Limits)")                                                                       ; Page Width changed, check limits
      (action_tile "Angle" "(Check-User-Input-Limits)")                                                                           ; Angle changed, check limits
      (action_tile "Scale" "(Check-User-Input-Limits)")                                                                           ; Scale changed, check limits
      (action_tile "HorizontalOffset" "(Check-User-Input-Limits)")                                                                ; Horizontal Offset changed, check limits
      (action_tile "VerticalOffset" "(Check-User-Input-Limits)")                                                                  ; Vertical Offset changed, check limits

      (action_tile "ImportPDF"                                                                                                    ; Import PDF button pressed, Get all values from dialog box
       "(setq Import T)
        (setq Existing (get_tile \"Existing\"))
        (setq New (get_tile \"New\"))
        (setq StartPage (atoi (get_tile \"StartPage\")))
        (setq EndPage (atoi (get_tile \"EndPage\")))
        (setq InsPtReq (atoi (get_tile \"InsPt\")))                 
        (setq Scale (atof (get_tile \"Scale\")))
        (setq Ang (atoi (get_tile \"Angle\")))
        (setq PageHeight (atoi (get_tile \"PageHeight\")))
        (setq PageWidth (atoi (get_tile \"PageWidth\")))
        (setq Rows (atoi (get_tile \"Rows\")))
        (setq HorOffset (atoi (get_tile \"HorizontalOffset\")))
        (setq VerOffset (atoi (get_tile \"VerticalOffset\")))
        (setq FilePath (get_tile \"FilePath\"))
        (done_dialog)"
      )
      
      (start_dialog)                                                                                                              ; Start the dialog box
      (unload_dialog Dcl-id)                                                                                                      ; Unload dialog box id
   )
  )
  
  (if (= New "1") (setq New T) (setq New nil))                                                                                    ; Convert to Bool
  (if (= Existing "1") (setq Existing T) (setq Existing nil))                                                                     ; Convert to Bool
  (if (<= Scale 0) (setq Scale 1))                                                                                                ; Force a scale of 1 if zero or negative number
  
  (if (and (= New T) (not (vl-file-directory-p FilePath)))                                                                        ; Check if Folder path doesn't exist
    (progn
      (setq Import nil)                                                                                                           ; Set the Import to false if the folder does not exist
      (princ (strcat "\nError: The folder does not exist: " FilePath))                                                            ; Print an error message showing the invalid folder path
    )
  )

  (if Import                                                                                                                      ; If a Import PDF button was pressed
    (progn
      (if (= InsPtReq 1)                                                                                                          ; If specify insertion point check box was enabled
        (setq InsPt (getpoint "\nSpecify insertion point: "))                                                                     ; Then prompt user to specify insertion point
        (setq InsPt '(0 0 0))                                                                                                     ; Else default to (0,0,0)
      )                                                                                                                           ; End If

      (if (not StartPage) (setq StartPage 1))                                                                                     ; Specify default value for variable if not defined
      (if (not EndPage) (setq EndPage 999))                                                                                       ; Specify default value for variable if not defined
      (if (not Scale) (setq Scale 1))                                                                                             ; Specify default value for variable if not defined
      (if (not Ang) (setq Ang 0))                                                                                                 ; Specify default value for variable if not defined
      (if (not PageHeight) (setq PageHeight 11))                                                                                  ; Specify default value for variable if not defined
      (if (not PageWidth) (setq PageWidth 17))                                                                                    ; Specify default value for variable if not defined
      (if (not Rows) (setq Rows 100))                                                                                             ; Specify default value for variable if not defined
      (if (not HorOffset) (setq HorOffset 0))                                                                                     ; Specify default value for variable if not defined
      (if (not VerOffset) (setq VerOffset 0))                                                                                     ; Specify default value for variable if not defined
      
      (if Existing                                                                                                                ; If Option A - Insert all PDF pages into the currently open drawing was selected
        (Insert-pdf-on-existing-drawing File StartPage EndPage Scale Ang PageHeight PageWidth Rows HorOffset VerOffset InsPt)     ; Call insert pdf on existing drawing function
      )                                                                                                                           ; End If

      (if New                                                                                                                     ; If Option B - Create a new drawing for each page of the PDF was selected
        (Insert-pdf-as-new-drawing File StartPage EndPage Scale Ang InsPt FilePath)                                               ; Call insert pdf on existing drawing function
      )                                                                                                                           ; End If
    )
  )
  
  (setvar "cmdecho" CmdEchoLastVal)                                                                                               ; Restore cmdecho variable to it's last value
  (princ)                                                                                                                         ; Suppress return value output to keep the command line clean
  
)

;END OF THE PROGRAM
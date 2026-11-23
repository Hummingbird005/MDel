# MDel – checklist UAT (mediul de test)

Rulați după instalare (vezi [DEPLOY.md](DEPLOY.md)), cu **căsuțe de test**. Folosiți cel puțin trei conturi:
**A** – administrator, **B** – aprobator (alt utilizator decât A), **C** – fără rol. Notați rezultatul (✅ / ❌)
și, la ❌, ce ați văzut (captură, mesaj, ora) – le folosim pentru corecturi.

## 1. Acces și interfață

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 1.1 | A deschide aplicația | intră fără parolă (Windows), vede Dashboard, Delegări, Istoric/Audit, Setări | |
| 1.2 | B deschide aplicația | vede Dashboard, Delegări, Aprobări; **nu** vede Setări / Audit | |
| 1.3 | C deschide aplicația | mesaj „Nu aveți drepturi”, nicio delegare vizibilă | |
| 1.4 | Comută tema (soare/lună) și reîncarcă | tema rămâne aleasă | |
| 1.5 | Schimbă limba în EN, apoi RO | interfața se traduce; rămâne după reîncărcare | |

## 2. Setări (A)

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 2.1 | Testează conexiunea AD, Exchange, SMTP (cu email de test), Fișiere | toate „OK”; emailul de test ajunge | |
| 2.2 | Exchange → verifică forwardul unei căsuțe de test | afișează forwardul curent (sau „–”) | |
| 2.3 | Grup de informare: adaugă un utilizator | apare în listă | |
| 2.4 | Salvează o valoare invalidă (ex. retenție 0) | mesaj de eroare pe câmp, nimic salvat | |
| 2.5 | Șabloane email: modificați „Cerere de aprobare” (subiect + text cu `{sursa}`, `{inceput}`) și salvați | previzualizarea arată textul; următoarea cerere sosește cu noul text (în română) | |

## 3. Delegare cu aprobare

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 3.1 | A: Delegare nouă – sursă X, destinatar Y, început mâine 09:00, sfârșit poimâine 18:00, fișier PDF | stare **În așteptare**; în panou „Înregistrat de: A” | |
| 3.2 | Emailuri | grupul de aprobatori primește cererea (cu link); grupul de informare – informare | |
| 3.3 | A încearcă să-și aprobe propria cerere (dacă A e și aprobator) | refuzat: „Nu vă puteți aproba propria cerere” | |
| 3.4 | B: Aprobări → Aprobă | stare **Programată**; „Aprobat de: B”; email către administratori, X, Y, grup informare | |
| 3.5 | Al doilea aprobator încearcă să aprobe aceeași cerere | „cererea a fost deja procesată” | |
| 3.6 | La ora de început (mâine 09:00, ± 30 s) | stare **Activă**; în Exchange forwardul X → Y este setat; email de activare | |
| 3.7 | Trimiteți un email către X | ajunge la Y; copie la X dacă „Forward + copie” | |
| 3.8 | La ora de sfârșit | stare **Expirată**; forwardul scos din Exchange (cel anterior restaurat); email la administratori, X, Y | |

## 4. Delegare fără aprobare și oprire

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 4.1 | A: Delegare nouă cu bifa **Fără aprobare**, început acum | stare **Activă** imediat; aprobatorii primesc doar informare | |
| 4.2 | A: Oprește delegarea, cu motiv | stare **Oprită**; forward eliminat în Exchange; email la administratori, X, Y | |
| 4.3 | Delegare fără aprobare cu început peste 1 oră | stare **Programată**, se activează singură la oră | |

## 5. Reguli și cazuri limită

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 5.1 | A doua delegare pe aceeași căsuță sursă, în același interval | refuzată, cu numărul delegării existente | |
| 5.2 | Forward invers (Y → X) în același interval | refuzat (buclă) | |
| 5.3 | Destinatar = sursă / dată sfârșit înainte de început | erori pe câmpuri | |
| 5.4 | Destinatar fără căsuță sau extern | nu apare în căutare / refuzat | |
| 5.5 | Căsuța sursă are deja un forward setat manual | avertisment în formular; după expirare forwardul vechi e restaurat | |
| 5.6 | În timp ce o delegare e activă, schimbați manual forwardul în Exchange | în max. 1 oră: avertisment pe delegare + email la administratori; la oprire nu e suprascris | |
| 5.7 | Fișier > limita setată / extensie nepermisă | refuzat cu mesaj | |
| 5.8 | Delegare activă: modifică doar data de sfârșit | se salvează; celelalte câmpuri sunt blocate | |

## 6. Dashboard, liste, audit

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 6.1 | Dashboard | cardurile KPI corecte; „Următoarele 7 zile” arată activările/expirările | |
| 6.2 | Delegări: filtre stare/mod, sortare, interval, căutare | lista se filtrează corect | |
| 6.3 | Export CSV | se deschide corect în Excel (diacritice, coloane) | |
| 6.4 | Istoric / Audit: filtrați după delegarea din pasul 3 | apar toate acțiunile ei (creare, aprobare, activare, expirare, emailuri) | |
| 6.5 | Audit: detaliul unei modificări de setări | valori vechi/noi; parolele mascate | |
| 6.6 | Setări → Notificări email | emailurile trimise; dacă există eșuate – Retrimite funcționează | |

## 7. Robustețe

| # | Pas | Rezultat așteptat | ✅/❌ |
|---|---|---|---|
| 7.1 | Opriți app pool-ul peste ora de început a unei delegări, porniți-l după | delegarea se activează la pornire | |
| 7.2 | Exchange indisponibil la ora de activare | stare **Eroare** + un email la administratori; se activează singură după revenirea Exchange (reîncercare la 10 min) | |
| 7.3 | SMTP indisponibil | delegările funcționează; emailurile apar „În coadă/Eșuate” și pleacă după revenire / Retrimite | |

**Feedback:** trimiteți tabelul completat, plus CSV-ul din `scripts/Test-MDelExchange.ps1` și exportul de audit
pentru perioada testului.

# 🎮 Pogađanje slova

Cilj igre je pogoditi reč, a da ne igrac ne iskoristi sve pokusaje

![guess-the-word](https://github.com/user-attachments/assets/ac5492fb-c9d4-4e42-9420-6aa3baff14a7)

---

## 🕹️ Tok igre

* **Sistem bira skrivenu reč** iz baze podataka ili unapred definisane liste.
* **Igrač unosi jedno slovo** po potezu putem tastature.
* **Otkrivanje pozicija:** Ako je slovo deo reči, prikazuju se sve njegove pozicije u reči.
* **Smanjenje pokušaja:** Ako slovo nije deo reči, broj preostalih pokušaja se smanjuje.
* **Kraj igre:** Igra se završava pobedom (sva slova pogođena) ili porazom (pokušaji istekli).

---

## 📜 Pravila

* ✅ **Jedno slovo:** Dozvoljen je unos samo jednog karaktera po potezu.
* 🚫 **Bez ponavljanja:** Ista slova se ne mogu pogađati više puta.
* 🔡 **Case-insensitive:** Velika i mala slova se tretiraju isto (npr. 'A' je isto što i 'a').
* ⏳ **Limit:** Broj pokušaja je ograničen na fiksni broj (npr. 6 ili 10).

"use strict";

const cards = Array.from(document.querySelectorAll(".recipe-card"));
const search = document.getElementById("recipe-search");
const sort = document.getElementById("recipe-sort");
const grid = document.getElementById("recipe-grid");
const tabs = Array.from(document.querySelectorAll(".category-tab"));
const chapterLinks = Array.from(document.querySelectorAll("[data-chapter]"));
const reset = document.getElementById("reset-filters");
const count = document.getElementById("result-count");
const printSelection = document.getElementById("print-selection");
let activeCategory = "all";
let printTarget = null;
let printState = null;
const normalize = value => value.normalize("NFKD").replace(/[\u0300-\u036f]/g, "").toLowerCase().replace(/[\u2018\u2019]/g, "'");
const searchableText = new Map(cards.map(card => [card, normalize(card.dataset.title + " " + card.dataset.category + " " + card.querySelector(".recipe-content").textContent)]));

function filterRecipes() {
    const terms = normalize(search.value).trim().split(/\s+/).filter(Boolean);
    let visible = 0;
    cards.forEach(card => {
        const matches = (activeCategory === "all" || card.dataset.category === activeCategory)
            && terms.every(term => searchableText.get(card).includes(term));
        card.hidden = !matches;
        if (matches) visible++;
    });
    tabs.forEach(tab => tab.setAttribute("aria-pressed", String(tab.dataset.category === activeCategory)));
    chapterLinks.forEach(link => {
        if (link.dataset.chapter === activeCategory) link.setAttribute("aria-current", "page");
        else link.removeAttribute("aria-current");
    });
    count.textContent = `Showing ${visible} of ${cards.length} recipes${activeCategory === "all" ? "" : " in " + activeCategory}`;
    printSelection.textContent = `Print selected recipes (${visible})`;
    printSelection.disabled = visible === 0;
    document.getElementById("empty-state").hidden = visible !== 0;
    reset.hidden = activeCategory === "all" && terms.length === 0;
}

function clearFilters() {
    activeCategory = "all";
    search.value = "";
    filterRecipes();
}

tabs.forEach(tab => tab.addEventListener("click", () => {
    activeCategory = tab.dataset.category;
    filterRecipes();
}));
chapterLinks.forEach(link => link.addEventListener("click", () => {
    activeCategory = link.dataset.chapter;
    search.value = "";
    filterRecipes();
}));
document.querySelector(".brand").addEventListener("click", clearFilters);
search.addEventListener("input", filterRecipes);
reset.addEventListener("click", clearFilters);
document.getElementById("empty-reset").addEventListener("click", clearFilters);
sort.addEventListener("change", () => {
    const ordered = [...cards];
    ordered.sort((a, b) => sort.value === "collection"
        ? cards.indexOf(a) - cards.indexOf(b)
        : a.dataset.title.localeCompare(b.dataset.title) * (sort.value === "za" ? -1 : 1));
    ordered.forEach(card => grid.append(card));
});

function preparePrint() {
    if (printState !== null) return;
    printState = cards.map(card => ({ card, open: card.open, hidden: card.hidden }));
    cards.forEach(card => {
        const selected = printTarget ? card === printTarget : !card.hidden;
        card.hidden = !selected;
        if (selected) card.open = true;
    });
}

function restorePrint() {
    if (printState === null) return;
    printState.forEach(state => {
        state.card.open = state.open;
        state.card.hidden = state.hidden;
    });
    printState = null;
    printTarget = null;
}

function requestPrint(card = null) {
    printTarget = card;
    preparePrint();
    try {
        window.print();
    } catch (error) {
        restorePrint();
        throw error;
    }
}

printSelection.addEventListener("click", () => requestPrint());
window.addEventListener("beforeprint", preparePrint);
window.addEventListener("afterprint", restorePrint);

cards.forEach(card => {
    card.querySelectorAll(".recipe-ingredient-items").forEach(list => {
        Array.from(list.children).forEach(item => {
            const label = document.createElement("label");
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            const text = document.createElement("span");
            Array.from(item.childNodes).forEach(child => {
                if (child.nodeName === "UL" || child.nodeName === "OL") return;
                if (child.nodeName === "P") {
                    while (child.firstChild) text.append(child.firstChild);
                    child.remove();
                } else {
                    text.append(child);
                }
            });
            label.append(checkbox, text);
            item.prepend(label);
        });
        list.classList.add("ingredient-list");
    });
    const tools = document.createElement("div");
    tools.className = "recipe-tools";
    const print = document.createElement("button");
    print.type = "button";
    print.className = "print-button";
    print.textContent = "Print this recipe";
    print.addEventListener("click", () => requestPrint(card));
    const uncheck = document.createElement("button");
    uncheck.type = "button";
    uncheck.className = "text-button";
    uncheck.textContent = "Reset ingredient checks";
    uncheck.addEventListener("click", () => {
        card.querySelectorAll('input[type="checkbox"]').forEach(checkbox => { checkbox.checked = false; });
    });
    const permalink = document.createElement("a");
    permalink.className = "text-button";
    permalink.href = "#" + card.id;
    permalink.textContent = "Recipe link";
    tools.append(print, uncheck, permalink);
    card.querySelector(".recipe-content").prepend(tools);
});

function openLinkedRecipe() {
    let id;
    try {
        id = decodeURIComponent(location.hash.slice(1));
    } catch (error) {
        console.error("Invalid recipe link fragment.", error);
        return;
    }
    const target = document.getElementById(id);
    const card = target?.closest(".recipe-card");
    if (!card) return;
    clearFilters();
    card.open = true;
    requestAnimationFrame(() => target.scrollIntoView({ block: "start" }));
}

window.addEventListener("hashchange", openLinkedRecipe);
document.getElementById("search-control").hidden = false;
document.getElementById("filter-controls").hidden = false;
printSelection.hidden = false;
filterRecipes();
openLinkedRecipe();

export function takeToken(input) {
    if (!(input instanceof HTMLInputElement) || input.type !== "password") {
        throw new Error("Access token input is unavailable.");
    }
    const token = input.value;
    input.value = "";
    return token;
}

const key = "jobhunter.rest-access.v1";
export function loadToken() { return localStorage.getItem(key); }
export function saveToken(token) { localStorage.setItem(key, token); }
export function forgetToken() { localStorage.removeItem(key); }
